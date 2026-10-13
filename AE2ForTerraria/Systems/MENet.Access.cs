using System.Collections.Generic;
using System.IO;
using System.Linq;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    internal const byte ClientTerminalRead = (MessageTag << ProtocolShift) | 40;
    internal const byte ClientInspectBridge = (MessageTag << ProtocolShift) | 41;
    internal const byte ClientDisconnectBridge = (MessageTag << ProtocolShift) | 42;
    internal const byte ServerBridgeAccess = (MessageTag << ProtocolShift) | 43;
    internal const byte ServerBridgeState = (MessageTag << ProtocolShift) | 44;
    internal const byte ServerTerminalSnapshot = (MessageTag << ProtocolShift) | 45;
    internal const byte ServerTerminalDenied = (MessageTag << ProtocolShift) | 46;
    private const byte ServerTerminalPolicy = (MessageTag << ProtocolShift) | 51;
    private static int EffectiveReadLevel(Point16 position, Player player)
    {
        var anchor = AnchorOf(position);
        if (anchor.X < 0) return NetworkRegistry.LevelNone;
        if (NetworkRegistry.GetOwner(anchor) == null && Microsoft.Xna.Framework.Vector2.DistanceSquared(player.Center,
            new Microsoft.Xna.Framework.Vector2(position.X*16f+8f,position.Y*16f+8f)) <= 512f*512f) return NetworkRegistry.LevelManage;
        return NetworkRegistry.GetLevel(anchor,player.name);
    }
    private sealed record ReadSession(Point16 Position, int Epoch, string Player);
    private static readonly Dictionary<int, ReadSession> ReadSessions = new();
    internal static bool IsNearbyLinkedEntrance(Point16 target, Point16 entrance, Player player)
    {
        if (player == null || !player.active || player.dead || !WorldGen.InWorld(entrance.X, entrance.Y, 0)) return false;
        Tile tile = Main.tile[entrance.X, entrance.Y];
        int type = tile.HasTile ? MachineTiles.CanonicalType(tile.TileType) : -1;
        if (type != ModContent.TileType<MECraftingTerminalTile>() && type != ModContent.TileType<MEAccessTerminalTile>()) return false;
        return Microsoft.Xna.Framework.Vector2.DistanceSquared(player.Center,
                new Microsoft.Xna.Framework.Vector2(entrance.X*16f+8f,entrance.Y*16f+8f)) <= 512f*512f
            && MENetwork.Collect(target.X,target.Y).Contains(entrance);
    }
    private static bool HasNearbyReadEntrance(Point16 target, int who, Player player)
        => ReadSessions.TryGetValue(who,out var session) && session.Player == player.name
            && IsNearbyLinkedEntrance(target,session.Position,player);
    internal static void RequestTerminalRead(Point16 position, int epoch)
    {
        if (!IsClient || epoch <= 0) return;
        var packet = CreatePacket(); packet.Write(ClientTerminalRead); packet.Write(position.X); packet.Write(position.Y); packet.Write(epoch); packet.Send();
    }
    internal static void RequestBridge(Point16 position, bool disconnect = false)
    {
        if (!IsClient) return;
        var packet = CreatePacket(); packet.Write(disconnect ? ClientDisconnectBridge : ClientInspectBridge);
        packet.Write(position.X); packet.Write(position.Y); packet.Send();
    }
    private static bool HandleAccessRequest(byte message, Point16 position, BinaryReader reader, int who)
    {
        if (message != ClientTerminalRead && message != ClientInspectBridge && message != ClientDisconnectBridge) return false;
        if (who < 0 || who >= Main.maxPlayers || Main.player[who] is not { active: true } player) return true;
        if (message == ClientTerminalRead)
        {
            int epoch = reader.ReadInt32(); if (epoch <= 0) return true;
            ReadSessions[who] = new(position, epoch, player.name);
            int tileType = WorldGen.InWorld(position.X, position.Y, 0) && Main.tile[position.X, position.Y].HasTile ? MachineTiles.CanonicalType(Main.tile[position.X, position.Y].TileType) : -1;
            if (tileType != ModContent.TileType<MECraftingTerminalTile>() && tileType != ModContent.TileType<MEAccessTerminalTile>()) { SendTerminalDenial(who, ReadSessions[who]); return true; }
            if (!CheckPermission(ClientRequestLedger, position, who)) { SendTerminalDenial(who, ReadSessions[who]); return true; }
            Subscribers[who] = position;
            SendAggregate(position, who);
            return true;
        }
        if (!BridgeRecovery.InRange(position, player) || !TileEntity.ByPosition.TryGetValue(position, out var entity)
            || entity is not MEWirelessBridgeEntity bridge || !bridge.IsTileValidForEntity(position.X, position.Y)) return true;
        if (message == ClientDisconnectBridge)
        {
            if (!BridgeRecovery.TryDisconnect(position, player)) Notify(who, "只能断开你在原网络有管理权限的无线桥");
            else { SyncBridge(position); Notify(who, "此无线桥已断开，网络恢复独立"); ValidateTerminalSubscriptions(); }
        }
        var anchor = AnchorOf(position);
        var response = CreatePacket(); response.Write(ServerBridgeAccess); response.Write(position.X); response.Write(position.Y);
        response.Write(bridge.Channel); response.Write(bridge.Enabled);
        response.Write(NetworkRegistry.GetLevel(anchor, player.name) >= NetworkRegistry.LevelManage);
        response.Write(NetworkRegistry.CanManageOriginalBlock(position, player.name)); response.Send(who);
        return true;
    }
    private static void SendTerminalDenial(int who, ReadSession session)
    {
        var packet = CreatePacket(); packet.Write(ServerTerminalDenied); packet.Write(session.Position.X); packet.Write(session.Position.Y);
        packet.Write(session.Epoch); packet.Send(who);
        ReadSessions.Remove(who); Subscribers.Remove(who);
    }
    private static void SendTerminalPolicy(Point16 position, int epoch, int who)
    {
        if (epoch <= 0) return;
        var anchor = AnchorOf(position);
        var locked = NetworkRegistry.GetLocked(anchor);
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        writer.Write(ServerTerminalPolicy); writer.Write(position.X); writer.Write(position.Y);
        writer.Write(epoch); writer.Write(anchor.X); writer.Write(anchor.Y); writer.Write(locked.Count);
        foreach (int type in locked) writer.Write(type);
        SendAuthorizedPayload(buffer.ToArray(), position, who, -1);
    }
    internal static void ValidateTerminalSubscriptions()
    {
        if (!IsServer) return;
        foreach (var pair in ReadSessions.ToArray())
        {
            int who = pair.Key; var session = pair.Value;
            if (who < 0 || who >= Main.maxPlayers || Main.player[who] is not { active: true } player || player.name != session.Player)
            { ReadSessions.Remove(who); Subscribers.Remove(who); continue; }
            var anchor = AnchorOf(session.Position);
            if (anchor.X < 0 || NetworkRegistry.GetLevel(anchor, player.name) < NetworkRegistry.LevelReadOnly) SendTerminalDenial(who, session);
        }
    }
    private static bool HandleAccessResponse(byte message, Point16 position, BinaryReader reader)
    {
        if (message == ServerTerminalPolicy)
        {
            int policyEpoch = reader.ReadInt32();
            var anchor = new Point16(reader.ReadInt16(), reader.ReadInt16());
            int policyCount = ReadCount(reader); var locked = new List<int>(policyCount);
            for (int i = 0; i < policyCount; i++)
            {
                int type = reader.ReadInt32();
                if (!SavedItemGuard.IsValidType(type)) throw new InvalidDataException("Invalid terminal locked item");
                locked.Add(type);
            }
            UISystem.ApplyTerminalPolicy(position, policyEpoch, anchor, locked); return true;
        }
        if (message == ServerBridgeAccess)
        {
            int channel = reader.ReadInt32(); bool enabled = reader.ReadBoolean(), manage = reader.ReadBoolean(), original = reader.ReadBoolean();
            UISystem.BridgeState?.Apply(position, channel, enabled, manage, original); return true;
        }
        if (message == ServerBridgeState)
        {
            int channel = reader.ReadInt32(); bool enabled = reader.ReadBoolean();
            if (TileEntity.ByPosition.TryGetValue(position, out var entity) && entity is MEWirelessBridgeEntity bridge)
            { bridge.SetChannel(channel); bridge.SetEnabled(enabled); }
            if (UISystem.BridgeState?.Position == position) RequestBridge(position);
            return true;
        }
        if (message == ServerTerminalDenied)
        { UISystem.ApplyTerminalAccess(position, reader.ReadInt32(), 0, null, new(-1, -1), new(-1, -1)); return true; }
        if (message != ServerTerminalSnapshot) return false;
        int epoch = reader.ReadInt32(), level = reader.ReadInt32();
        var cpu = new Point16(reader.ReadInt16(), reader.ReadInt16()); var assembler = new Point16(reader.ReadInt16(), reader.ReadInt16());
        int count = ReadCount(reader); var totals = new Dictionary<ItemKey, long>(count);
        for (int i = 0; i < count; i++) { var key = ReadKey(reader); long amount = reader.ReadInt64(); if (!key.IsAir && amount > 0) totals[key] = amount; }
        UISystem.ApplyTerminalAccess(position, epoch, level, totals, cpu, assembler); return true;
    }
}

internal sealed class TerminalAccessSystem : ModSystem
{
    private int _ticks;
    public override void PostUpdateEverything() { if (++_ticks >= 30) { _ticks = 0; MENet.ValidateTerminalSubscriptions(); MENet.ExpirePartialSnapshots(); } }
}
