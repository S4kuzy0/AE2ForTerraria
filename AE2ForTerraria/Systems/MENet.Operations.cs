using System;
using System.Collections.Generic;
using System.IO;
using AE2ForTerraria.Core;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.DataStructures;

namespace AE2ForTerraria.Systems;

public static partial class MENet
{
    public const byte ClientDepositScope = (MessageTag << ProtocolShift) | 29;
    public const byte ClientSlotAction = (MessageTag << ProtocolShift) | 30;

    public static bool CursorPending;
    internal const byte ClientDepositCursor = (MessageTag << ProtocolShift) | 52;
    private static long _cursorSequence, _pendingCursor;
    private static Player _pendingCursorPlayer;
    private static readonly Dictionary<int, long> LastCursorRequest = new();

    internal static long BeginCursorTransaction()
    {
        if (!IsClient || CursorPending || InventoryTransactionPending || Main.LocalPlayer.HasLockedInventory()
            || _cursorSequence == long.MaxValue) return 0;
        _pendingCursor = ++_cursorSequence;
        _pendingCursorPlayer = Main.LocalPlayer;
        CursorPending = true;
        return _pendingCursor;
    }

    internal static void ResetCursorTransactions()
    {
        CursorPending = false;
        _pendingCursor = 0;
        _pendingCursorPlayer = null;
        LastCursorRequest.Clear();
        // Keep the sequence across reconnects: an old completion must not match a new operation.
    }

    internal static void ForgetCursorPlayer(int who) => LastCursorRequest.Remove(who);

    private static void ReceiveCursor(BinaryReader reader)
    {
        long request = reader.ReadInt64();
        if (!CursorPending || request <= 0 || request != _pendingCursor
            || !ReferenceEquals(_pendingCursorPlayer, Main.LocalPlayer))
        { reader.BaseStream.Seek(0, SeekOrigin.End); return; }
        int length = reader.ReadInt32();
        if (length <= 0 || length > MaxSnapshotBytes || length != reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid cursor item payload length");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("Truncated cursor item payload");
        // ItemIO catches some mod NetReceive IO errors internally. Check the whole
        // server-written item envelope before invoking those callbacks on a partial item.
        using var buffer = new MemoryStream(bytes, false);
        using var itemReader = new BinaryReader(buffer);
        Item cursor = Terraria.ModLoader.IO.ItemIO.Receive(itemReader, true, true);
        if (buffer.Position != buffer.Length) throw new InvalidDataException("Trailing cursor item data");
        Item mirror = cursor.Clone();
        Main.mouseItem = cursor;
        Main.LocalPlayer.inventory[58] = cursor;
        Main.clientPlayer.inventory[58] = mirror;
        CursorPending = false;
        _pendingCursor = 0;
        _pendingCursorPlayer = null;
    }

    internal static void DepositCursor(Point16 position)
    {
        long request = BeginCursorTransaction();
        if (request == 0) return;
        Main.LocalPlayer.inventory[58] = Main.mouseItem;
        NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, Main.myPlayer, 58);
        var packet = CreatePacket(); packet.Write(ClientDepositCursor); packet.Write(position.X); packet.Write(position.Y);
        packet.Write(request); packet.Send();
    }

    internal static long ApplyCursorDeposit(Point16 position, Player player)
    {
        if (!WorldGen.InWorld(position.X,position.Y,0)) return 0;
        int type = Main.tile[position.X,position.Y].HasTile ? MachineTiles.CanonicalType(Main.tile[position.X,position.Y].TileType) : -1;
        if (type != Terraria.ModLoader.ModContent.TileType<MEAccessTerminalTile>()
            && type != Terraria.ModLoader.ModContent.TileType<MECraftingTerminalTile>()) return 0;
        return MENetwork.Deposit(MENetwork.CollectStorage(position.X,position.Y),player.inventory[58]);
    }

    private static void HandleCursorRequest(byte message, Point16 position, BinaryReader reader, int who)
    {
        long request = reader.ReadInt64();
        if(who<0 || who>=Main.maxPlayers || Main.player[who]?.active!=true) return;
        try
        {
            if (request <= 0 || LastCursorRequest.TryGetValue(who, out long previous) && request <= previous) return;
            LastCursorRequest[who] = request;
            int slot = 0; bool station = false, right = false;
            if (message == ClientSlotAction)
            { slot = reader.ReadInt32(); station = reader.ReadBoolean(); right = reader.ReadBoolean(); }
            if (!CheckPermission(message,position,who)) return;
            if (message == ClientSlotAction) ApplySlot(position,slot,station,right,who);
            else
            {
                var preview = StorageOperations.PreviewDeposit(MENetwork.CollectStorage(position.X, position.Y), Main.player[who].inventory[58]);
                long moved=ApplyCursorDeposit(position,Main.player[who]);
                Notify(who,moved>0 ? "已存入 "+moved+" 件" + (moved < preview.Requested ? "；其余保留在手中。" + preview.Reason : "")
                    : preview.Accepted > 0 ? "未存入：目标已变化，请重新打开访问终端" : preview.Message);
                SendNetworkLedgers(position.X,position.Y);
                if (moved > 0) NetMessage.SendData(MessageID.SyncEquipment,-1,-1,null,who,58);
            }
        }
        finally
        {
            using var itemBuffer = new MemoryStream();
            using var itemWriter = new BinaryWriter(itemBuffer);
            Terraria.ModLoader.IO.ItemIO.Send(Main.player[who].inventory[58], itemWriter, true, true);
            using var buffer = new MemoryStream();
            using var packet = new BinaryWriter(buffer);
            packet.Write(ServerCursor); packet.Write((short)0); packet.Write((short)0);
            packet.Write(request);
            packet.Write(checked((int)itemBuffer.Length));
            packet.Write(itemBuffer.GetBuffer(), 0, checked((int)itemBuffer.Length));
            SendPayload(buffer.ToArray(), who, -1);
        }
    }
    public static void Reset() { Subscribers.Clear(); ReadSessions.Clear(); ClearInventoryPages(); ClearFragments(); ResetStorageUpgrades(); ResetInventoryDeposits(); ResetCursorTransactions(); _loggedProtocolMismatch = false; }

    public static void DepositScope(Point16 position, int slot, ItemKey expected, bool sameType, bool extras)
    {
        if (!IsClient) return;
        Player player = Main.LocalPlayer;
        if (slot < 0 || slot >= InventoryScope.DepositSlotCount(player, extras)
            || !ItemKey.FromItem(player.inventory[slot]).Equals(expected)) return;
        SubmitInventoryDeposit(position, extras, sameType ? -1 : slot, sameType ? expected.Type : 0);
    }

    public static bool SlotAction(Point16 position, int slot, bool station, bool right)
    {
        if (!IsClient) return false;
        long request = BeginCursorTransaction();
        if (request == 0) return true;
        // Vanilla inventory slot 58 is the cursor item. Sync the real player slot before submitting an intent.
        Main.LocalPlayer.inventory[58] = Main.mouseItem;
        NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, Main.myPlayer, 58);
        var packet = CreatePacket();
        packet.Write(ClientSlotAction); packet.Write(position.X); packet.Write(position.Y);
        packet.Write(request);
        packet.Write(slot); packet.Write(station); packet.Write(right); packet.Send();
        return true;
    }

    private static void ApplySlot(Point16 position, int slot, bool station, bool right, int who)
    {
        if (!TileEntity.ByPosition.TryGetValue(position, out var entity)) return;
        Item[] slots = station ? (entity is MECraftingTerminalEntity terminal ? terminal.Stations : (entity as MEMolecularAssemblerEntity)?.Stations) : (entity as MEDiskDriveEntity)?.Disks;
        if (slots == null || slot < 0 || slot >= slots.Length) return;
        Player player = Main.player[who];
        Item cursor = player.inventory[58];
        if (!ContainerSlotOperations.TryChange(slots, slot, ref cursor, right, entity as MEDiskDriveEntity, out string reason))
        { if (reason != null) Notify(who, reason); return; }
        player.inventory[58] = cursor;
        NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, who, 58);
        if (entity is MECraftingTerminalEntity changed) { changed.EnsureStationSlots(); changed.InvalidateStations(); SendTerminalStations(position); }
        else if (station) SendAssembler(position); else SendDriveSnapshot(position);
        SendNetworkLedgers(position.X,position.Y);
    }
    public const byte ServerCursor = (MessageTag << ProtocolShift) | 35;
}
