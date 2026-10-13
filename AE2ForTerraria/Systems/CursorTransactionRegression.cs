using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Net;
using Terraria.Net.Sockets;

namespace AE2ForTerraria.Systems;

/// <summary>Actual server requests and captured acknowledgement bytes, with disconnected in-memory players.</summary>
internal static class CursorTransactionRegression
{
    private sealed class ReplySink : ISocket
    {
        public readonly List<byte[]> Packets = new();
        public void AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state = null)
        { Packets.Add(data.AsSpan(offset, size).ToArray()); callback?.Invoke(state); }
        public bool IsConnected() => false;
        public bool IsDataAvailable() => false;
        public void Close() { }
        public void SendQueuedPackets() { }
        public void StopListening() { }
        public RemoteAddress GetRemoteAddress() => null;
        public void Connect(RemoteAddress address) => throw new NotSupportedException("Cursor fixture must not connect.");
        public bool StartListening(SocketConnectionAccepted callback) => throw new NotSupportedException("Cursor fixture must not listen.");
        public void AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state = null)
            => throw new NotSupportedException("Cursor fixture must not receive live traffic.");
    }

    internal static void Run(Action<bool, string> check)
    {
        if (!RegressionChecks.IsRunning || !Main.gameMenu || MENet.CursorPending || MENet.InventoryTransactionPending)
            throw new InvalidOperationException("Cursor checks require an idle isolated menu runner.");
        for (int i = 0; i < Main.maxPlayers; i++)
            if (Main.player[i]?.active == true) throw new InvalidOperationException("Cursor checks refuse active players.");
        int oldMode = Main.netMode, oldMyPlayer = Main.myPlayer;
        Player oldPlayer = Main.player[0], oldVisitor = Main.player[1], oldClientPlayer = Main.clientPlayer;
        Item oldMouse = Main.mouseItem;
        var oldRemotes = (RemoteClient[])Netplay.Clients.Clone();
        var oldBuffers = (MessageBuffer[])NetMessage.buffer.Clone();
        var temporaryBuffers = new MessageBuffer[NetMessage.buffer.Length];
        Mod mod = ModContent.GetInstance<AE2ForTerraria>();
        var netId = typeof(Mod).GetField("netID", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Mod.netID");
        var netMods = typeof(ModNet).GetField("netMods", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("ModNet.netMods");
        object oldNetId = netId.GetValue(mod), oldNetMods = netMods.GetValue(null);
        var sink = new ReplySink();
        var client = new Player { whoAmI = 0, active = true, name = "CursorRegressionOwner" };
        var server = new Player { whoAmI = 0, active = true, name = client.name };
        using var world = new RegressionWorld();
        var drive = world.Place(new MEDiskDriveEntity(), ModContent.TileType<MEDiskDriveTile>(), 20, 20);
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 40, 40);
        var terminal = new Point16(41, 40);
        world.SetTile(terminal.X, terminal.Y, ModContent.TileType<MEAccessTerminalTile>());
        NetworkRegistry.TryClaim(MENetwork.CanonicalAnchor(MENetwork.Collect(20, 20)), server.name);
        NetworkRegistry.TryClaim(MENetwork.CanonicalAnchor(MENetwork.Collect(40, 40)), server.name);

        void Client()
        { Main.netMode = NetmodeID.MultiplayerClient; Main.myPlayer = 0; Main.player[0] = client; }
        long Begin(Item item)
        {
            Client(); Main.mouseItem = item.Clone(); client.inventory[58] = Main.mouseItem;
            server.inventory[58] = item.Clone();
            return MENet.BeginCursorTransaction();
        }
        byte[] Execute(byte[] payload)
        {
            Main.netMode = NetmodeID.Server; Main.myPlayer = 255; Main.player[0] = server;
            sink.Packets.Clear();
            using var reader = new BinaryReader(new MemoryStream(payload));
            MENet.HandlePayload(reader, 0);
            // A one-mod net table uses one byte for the negotiated mod ID.
            return sink.Packets.Single(raw => raw.Length > 7 && raw[2] == MessageID.ModPacket
                && raw[3] == 0 && raw[7] == MENet.ServerCursor)[4..];
        }
        void Reply(byte[] envelope)
        {
            Client(); using var reader = new BinaryReader(new MemoryStream(envelope));
            MENet.Handle(reader, 256);
        }
        try
        {
            for (int i = 0; i < Netplay.Clients.Length; i++)
                Netplay.Clients[i] = new RemoteClient { State = i == 0 ? 10 : 0, Socket = i == 0 ? sink : new ReplySink() };
            for (int i = 0; i < NetMessage.buffer.Length; i++)
                NetMessage.buffer[i] = temporaryBuffers[i] = new MessageBuffer { whoAmI = i };
            netMods.SetValue(null, new[] { mod }); netId.SetValue(mod, (short)0);
            Main.clientPlayer = new Player();
            MENet.ResetCursorTransactions();

            int diskType = ModContent.ItemType<MEDisk1K>();
            long first = Begin(new Item(diskType));
            check(first > 0 && MENet.BeginCursorTransaction() == 0 && MENet.CursorPending,
                "cursor nonce admits only one outstanding operation");
            // A previous read denial can arrive after a new cursor request was sent.
            using (var stream = new MemoryStream())
            {
                using var writer = new BinaryWriter(stream);
                writer.Write(MENet.ServerDenied); writer.Write((short)0); writer.Write((short)0); writer.Write("permission");
                stream.Position = 0; using var reader = new BinaryReader(stream);
                MENet.HandlePayload(reader, 256);
            }
            check(MENet.CursorPending && Main.mouseItem.type == diskType,
                "unrelated permission denial cannot release a cursor operation or enable intervening inventory edits");

            byte[] request = Request(MENet.ClientSlotAction, drive.Position, first, 0, false, false);
            byte[] firstAck = Execute(request);
            check(drive.Disks[0].type == diskType && server.inventory[58].IsAir,
                "real server slot request moves the physical disk exactly once");
            Reply(firstAck);
            check(!MENet.CursorPending && Main.mouseItem.IsAir && client.inventory[58].IsAir && Main.clientPlayer.inventory[58].IsAir,
                "matching cursor acknowledgement publishes cursor and both native inventory mirrors together");

            long second = Begin(new Item(ItemID.Wood, 9));
            Reply(firstAck);
            check(MENet.CursorPending && Main.mouseItem.type == ItemID.Wood && Main.mouseItem.stack == 9,
                "late duplicate cursor acknowledgement neither replaces a newer held item nor completes its request");
            byte[] replayAck = Execute(request);
            check(drive.Disks[0].type == diskType && server.inventory[58].type == ItemID.Wood && server.inventory[58].stack == 9,
                "replayed slot nonce cannot swap a newly acquired cursor item with the installed disk");
            Reply(replayAck);
            check(MENet.CursorPending && Main.mouseItem.stack == 9,
                "server replay refusal acknowledges only the old nonce and leaves the current client operation pending");

            storage.Ledger.MaxAmountPerType = 3;
            byte[] deposit = Request(MENet.ClientDepositCursor, terminal, second);
            byte[] partialAck = Execute(deposit);
            var wood = ItemKey.Crafted(ItemID.Wood);
            check(storage.Ledger.GetAmount(wood) == 3 && server.inventory[58].stack == 6,
                "cursor deposit nonce route preserves the exact partial-capacity remainder");
            Reply(partialAck);
            check(!MENet.CursorPending && Main.mouseItem.stack == 6 && Main.clientPlayer.inventory[58].stack == 6,
                "partial deposit acknowledgement agrees across all cursor mirrors");
            server.inventory[58] = new Item(ItemID.Wood, 4);
            storage.Ledger.Extract(wood, 3);
            Reply(Execute(deposit));
            check(storage.Ledger.GetAmount(wood) == 0 && server.inventory[58].stack == 4 && Main.mouseItem.stack == 6,
                "replayed cursor deposit never consumes a replacement held stack after capacity becomes free");

            long denied = Begin(new Item(diskType));
            server.name = "CursorRegressionVisitor";
            byte[] deniedAck = Execute(Request(MENet.ClientSlotAction, drive.Position, denied, 0, false, false));
            check(drive.Disks[0].type == diskType && server.inventory[58].type == diskType,
                "permission denial leaves both authoritative disk and cursor unchanged");
            Reply(deniedAck);
            check(!MENet.CursorPending && Main.mouseItem.type == diskType,
                "denied slot request still receives its own authoritative completion and releases its input lock");
            server.name = client.name;

            foreach (int slot in new[] { -1, int.MaxValue })
            {
                long invalid = Begin(new Item(ItemID.Gel, 7));
                Reply(Execute(Request(MENet.ClientSlotAction, drive.Position, invalid, slot, false, false)));
                check(!MENet.CursorPending && Main.mouseItem.type == ItemID.Gel && Main.mouseItem.stack == 7,
                    "out-of-range slot request returns an unchanged matching cursor completion");
            }
            long truncated = Begin(new Item(ItemID.Gel, 7));
            byte[] malformed = Request(MENet.ClientSlotAction, drive.Position, truncated, 0, false, false)[..^1];
            Reply(Execute(malformed));
            check(!MENet.CursorPending && Main.mouseItem.stack == 7 && drive.Disks[0].type == diskType,
                "truncated slot payload after a valid nonce cannot mutate items and still completes its own request");

            long tornReply = Begin(new Item(ItemID.Gel, 7));
            byte[] validReply = Execute(Request(MENet.ClientSlotAction, drive.Position, tornReply, -1, false, false));
            Reply(validReply[..^1]);
            check(MENet.CursorPending && Main.mouseItem.stack == 7,
                "truncated cursor acknowledgement cannot publish a partial item or release its pending transaction");
            foreach (int invalidLength in new[] { -1, 0, int.MaxValue, validReply.Length - 19, validReply.Length - 21 })
            {
                byte[] malformedLength = (byte[])validReply.Clone();
                // Envelope, message and position occupy eight bytes; nonce occupies the next eight.
                using (var stream = new MemoryStream(malformedLength, true))
                {
                    stream.Position = 16;
                    using var writer = new BinaryWriter(stream);
                    writer.Write(invalidLength);
                }
                Reply(malformedLength);
                check(MENet.CursorPending && Main.mouseItem.type == ItemID.Gel && Main.mouseItem.stack == 7,
                    "invalid cursor envelope length is refused before native item callbacks and preserves pending ownership");
            }
            Reply(validReply);
            check(!MENet.CursorPending && Main.mouseItem.stack == 7,
                "a complete matching acknowledgement can finish after a rejected malformed reply");

            long disconnected = Begin(new Item(ItemID.Gel, 7));
            byte[] oldAck = Execute(Request(MENet.ClientSlotAction, drive.Position, disconnected, -1, false, false));
            Client(); MENet.ResetCursorTransactions();
            long reconnected = Begin(new Item(ItemID.StoneBlock, 5));
            Reply(oldAck);
            check(reconnected > disconnected && MENet.CursorPending && Main.mouseItem.type == ItemID.StoneBlock,
                "disconnect reset releases old ownership without recycling a nonce into the next session");
            MENet.ResetCursorTransactions();
            Reply(oldAck);
            check(!MENet.CursorPending && Main.mouseItem.type == ItemID.StoneBlock,
                "unsolicited cursor acknowledgement cannot replace the held item when no operation is pending");

            SecuritySnapshotChecks(check, drive.Position, server, client, sink);
        }
        finally
        {
            MENet.ResetCursorTransactions();
            Main.player[0] = oldPlayer; Main.player[1] = oldVisitor; Main.clientPlayer = oldClientPlayer; Main.mouseItem = oldMouse;
            Array.Copy(oldRemotes, Netplay.Clients, oldRemotes.Length);
            Array.Copy(oldBuffers, NetMessage.buffer, oldBuffers.Length);
            foreach (var buffer in temporaryBuffers) { buffer?.reader?.Dispose(); buffer?.writer?.Dispose(); }
            netId.SetValue(mod, oldNetId); netMods.SetValue(null, oldNetMods);
            Main.myPlayer = oldMyPlayer; Main.netMode = oldMode;
        }
    }

    private static void SecuritySnapshotChecks(Action<bool, string> check, Point16 position,
        Player server, Player client, ReplySink ownerSink)
    {
        var visitor = new Player { whoAmI = 1, active = true, name = "CursorRegressionReader" };
        var visitorSink = new ReplySink();
        Main.netMode = NetmodeID.Server; Main.myPlayer = 255; Main.player[0] = server; Main.player[1] = visitor;
        Netplay.Clients[1] = new RemoteClient { State = 10, Socket = visitorSink };
        Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(position.X, position.Y));
        NetworkRegistry.SetLevel(anchor, visitor.name, NetworkRegistry.LevelReadOnly);
        NetworkRegistry.RecordAudit(anchor, server.name, "withdraw", "private-item", 7);
        NetworkRegistry.SetLocked(anchor, ItemID.Wood, true);

        byte[] Snapshot(ReplySink sink) => sink.Packets.Single(raw => raw.Length > 7
            && raw[2] == MessageID.ModPacket && raw[3] == 0 && raw[7] == MENet.ServerSyncSecurity)[4..];
        void Apply(byte[] envelope)
        {
            Main.netMode = NetmodeID.MultiplayerClient; Main.myPlayer = 0; Main.player[0] = client;
            using var reader = new BinaryReader(new MemoryStream(envelope)); MENet.Handle(reader, 256);
        }
        void SeedOldView()
        {
            NetworkRegistry.ApplyRemoteSecurity(anchor, server.name,
                new() { [visitor.name] = NetworkRegistry.LevelReadOnly });
            NetworkRegistry.ApplyRemoteAudit(anchor, new() { "previous-audit" });
            NetworkRegistry.ApplyRemoteLocked(anchor, new() { ItemID.Gel });
        }
        bool OldViewIntact() => NetworkRegistry.GetLevel(anchor, visitor.name) == NetworkRegistry.LevelReadOnly
            && NetworkRegistry.GetAudit(anchor).SequenceEqual(new[] { "previous-audit" })
            && NetworkRegistry.IsLocked(anchor, ItemID.Gel) && !NetworkRegistry.IsLocked(anchor, ItemID.Wood);

        ownerSink.Packets.Clear(); visitorSink.Packets.Clear();
        MENet.SyncSecurity(anchor);
        byte[] readable = Snapshot(visitorSink);
        var granted = ReadPolicy(readable);
        check(granted.Acl == 1 && granted.Audit == 1 && granted.Locks == 1 && Snapshot(ownerSink).Length > 0,
            "security snapshot reaches every authorized recipient even without an inventory subscription");

        NetworkRegistry.SetLevel(anchor, visitor.name, NetworkRegistry.LevelNone);
        ownerSink.Packets.Clear(); visitorSink.Packets.Clear();
        MENet.SyncSecurity(anchor);
        byte[] denied = Snapshot(visitorSink), ownerView = Snapshot(ownerSink);
        var redacted = ReadPolicy(denied); var full = ReadPolicy(ownerView);
        check(redacted.Owner == server.name && redacted.Acl == 0 && redacted.Audit == 0 && redacted.Locks == 0
            && full.Audit == 1 && full.Locks == 1,
            "revoked or unauthorized clients receive only ownership denial, never private ACL, audit or locked item data");
        ownerSink.Packets.Clear(); visitorSink.Packets.Clear();
        MENet.SyncSecurity(anchor, 1);
        check(ownerSink.Packets.Count == 0 && ReadPolicy(Snapshot(visitorSink)).Audit == 0,
            "targeted security reply honors its recipient and cannot bypass the read-permission filter");

        SeedOldView(); Apply(denied);
        check(NetworkRegistry.GetLevel(anchor, visitor.name) == NetworkRegistry.LevelNone
            && NetworkRegistry.GetAudit(anchor).Count == 0 && NetworkRegistry.GetLocked(anchor).Count == 0,
            "revocation reply clears a previous grant, private audit and locked-item cache atomically");
        SeedOldView(); Apply(ownerView[..^1]);
        check(OldViewIntact(), "truncated final lock entry cannot partially replace ACL or audit from a security snapshot");

        using (var stream = new MemoryStream())
        {
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)0); writer.Write(MENet.ProtocolVersion); writer.Write(MENet.ServerSyncSecurity);
            writer.Write(anchor.X); writer.Write(anchor.Y); writer.Write(server.name);
            writer.Write(-1); writer.Write(0); writer.Write(0);
            Apply(stream.ToArray());
        }
        check(OldViewIntact(), "negative ACL length is rejected without publishing an empty permission table");
        using (var stream = new MemoryStream())
        {
            using var writer = new BinaryWriter(stream);
            writer.Write((byte)0); writer.Write(MENet.ProtocolVersion); writer.Write(MENet.ServerSyncSecurity);
            writer.Write(anchor.X); writer.Write(anchor.Y); writer.Write(server.name);
            writer.Write(1); writer.Write(visitor.name); writer.Write(NetworkRegistry.LevelManage + 1);
            writer.Write(0); writer.Write(0);
            Apply(stream.ToArray());
        }
        check(OldViewIntact(), "out-of-range permission level cannot elevate the cached client permission view");
        Apply(ownerView);
        check(NetworkRegistry.GetLevel(anchor, visitor.name) == NetworkRegistry.LevelNone
            && NetworkRegistry.GetAudit(anchor).Count == 1 && NetworkRegistry.GetAudit(anchor)[0].Contains("private-item")
            && NetworkRegistry.IsLocked(anchor, ItemID.Wood) && !NetworkRegistry.IsLocked(anchor, ItemID.Gel),
            "complete security snapshot publishes all three policy sections consistently");
    }

    private static (string Owner, int Acl, int Audit, int Locks) ReadPolicy(byte[] envelope)
    {
        using var reader = new BinaryReader(new MemoryStream(envelope));
        if (reader.ReadByte() != 0 || reader.ReadUInt16() != MENet.ProtocolVersion || reader.ReadByte() != MENet.ServerSyncSecurity)
            throw new InvalidDataException("Unexpected captured security reply envelope");
        reader.ReadInt16(); reader.ReadInt16(); string owner = reader.ReadString();
        int acl = reader.ReadInt32();
        for (int i = 0; i < acl; i++) { reader.ReadString(); reader.ReadInt32(); }
        int audit = reader.ReadInt32();
        for (int i = 0; i < audit; i++) reader.ReadString();
        int locks = reader.ReadInt32();
        for (int i = 0; i < locks; i++) reader.ReadInt32();
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing security reply data");
        return (owner, acl, audit, locks);
    }

    private static byte[] Request(byte message, Point16 position, long nonce, int slot = 0, bool station = false, bool right = false)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(message); writer.Write(position.X); writer.Write(position.Y); writer.Write(nonce);
        if (message == MENet.ClientSlotAction) { writer.Write(slot); writer.Write(station); writer.Write(right); }
        return stream.ToArray();
    }
}
