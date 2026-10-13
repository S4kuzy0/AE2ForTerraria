using System;
using System.IO;
using System.Reflection;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Net;
using Terraria.Net.Sockets;

namespace AE2ForTerraria.Systems;

/// <summary>Actual intent decoding and native inventory receivers, without sockets or saved characters.</summary>
internal static class InventoryDepositRegression
{
    // ModPacket.Send(toClient) intentionally does not check IsConnected before
    // accessing Socket. A bare RemoteClient has a null Socket at recipe-load time.
    // Keep this in-memory receiver explicit; never connect a socket during tests.
    private sealed class ReplySink : ISocket
    {
        public void AsyncSend(byte[] data, int offset, int size, SocketSendCallback callback, object state = null)
            => callback?.Invoke(state);
        public bool IsConnected() => false;
        public bool IsDataAvailable() => false;
        public void Close() { }
        public void SendQueuedPackets() { }
        public void StopListening() { }
        public RemoteAddress GetRemoteAddress() => null;
        public void Connect(RemoteAddress address) => throw new NotSupportedException("Regression socket must not connect.");
        public bool StartListening(SocketConnectionAccepted callback) => throw new NotSupportedException("Regression socket must not listen.");
        public void AsyncReceive(byte[] data, int offset, int size, SocketReceiveCallback callback, object state = null)
            => throw new NotSupportedException("Regression socket must not receive live traffic.");
    }

    public static void Run(Action<bool, string> check)
    {
        if (!RegressionChecks.IsRunning) throw new InvalidOperationException("Deposit checks require the isolated runner.");
        for (int i = 0; i < Main.maxPlayers; i++)
            if (Main.player[i]?.active == true) throw new InvalidOperationException("Deposit checks refuse active players.");
        int oldMode = Main.netMode, oldMyPlayer = Main.myPlayer;
        bool oldServerSideCharacter = Main.ServerSideCharacter;
        Player oldPlayer = Main.player[0], oldClientPlayer = Main.clientPlayer;
        var oldRemotes = (RemoteClient[])Netplay.Clients.Clone();
        var oldBuffers = (MessageBuffer[])NetMessage.buffer.Clone();
        var temporaryBuffers = new MessageBuffer[NetMessage.buffer.Length];
        Mod mod = ModContent.GetInstance<AE2ForTerraria>();
        FieldInfo netIdField = typeof(Mod).GetField("netID", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate temporary regression mod network identity.");
        object oldNetId = netIdField.GetValue(mod);
        FieldInfo netModsField = typeof(ModNet).GetField("netMods", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Could not locate temporary regression mod network table.");
        object oldNetMods = netModsField.GetValue(null);
        var server = new Player { whoAmI = 0, active = true, name = "DepositRegressionOwner" };
        var client = new Player { whoAmI = 0, active = true, name = server.name };
        ItemKey wood = ItemKey.Crafted(ItemID.Wood);
        using var world = new RegressionWorld();
        var unit = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 30, 30);
        Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(30, 30));
        NetworkRegistry.TryClaim(anchor, server.name);
        void Client()
        { Main.netMode = NetmodeID.MultiplayerClient; Main.myPlayer = 0; Main.player[0] = client; }
        void Server()
        { Main.netMode = NetmodeID.Server; Main.myPlayer = 255; Main.player[0] = server; }
        void Setup(params (int slot, Item item)[] items)
        {
            MENet.ResetInventoryDeposits(); MENet.ResetStorageUpgrades(); MENet.CursorPending = false;
            unit.Ledger.Clear(); unit.Ledger.MaxBytes = 8192;
            server.name = "DepositRegressionOwner";
            for (int slot = 0; slot < client.inventory.Length; slot++)
            {
                client.inventory[slot] = new Item(); server.inventory[slot] = new Item();
                client.inventoryChestStack[slot] = false;
            }
            foreach (var (slot, item) in items)
            { client.inventory[slot] = item.Clone(); server.inventory[slot] = item.Clone(); }
            Client();
        }
        void Execute(MENet.InventoryDepositRequest intent, byte[] bytes = null)
        {
            Server();
            bytes ??= Encode(unit.Position, intent);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            MENet.HandlePayload(reader, 0);
        }
        void Reply(MENet.InventoryDepositRequest intent)
        {
            Client();
            for (int slot = 0; slot < InventoryScope.ExtendedSlots; slot++)
                if ((intent.Slots & (1UL << slot)) != 0) ReceiveEquipment(256, slot, server.inventory[slot]);
            Complete(intent.Nonce);
        }
        try
        {
            // A menu client has not initialized the server's Clients table, and its
            // mod has no negotiated netID yet. Build an entirely isolated, disconnected
            // server envelope so the exact same handlers/native writers run in either host.
            for (int index=0; index<Netplay.Clients.Length; index++)
                Netplay.Clients[index] = new RemoteClient { State=index==0 ? 10 : 0, Socket=new ReplySink() };
            for (int index=0; index<NetMessage.buffer.Length; index++)
                NetMessage.buffer[index] = temporaryBuffers[index] = new MessageBuffer { whoAmI=index };
            netModsField.SetValue(null, new[] { mod });
            netIdField.SetValue(mod, (short)0);
            check(ModNet.NetModCount==1 && ReferenceEquals(ModNet.GetMod(0),mod),
                "isolated deposit envelope initializes the actual mod packet identity table before dispatch");
            Main.ServerSideCharacter = false;
            Main.clientPlayer = new Player();
            Setup((0, new Item(ItemID.Wood, 10)));
            var intent = MENet.BeginInventoryDeposit(client, false, onlySlot: 0);
            check(intent != null && intent.Slots == 1 && client.inventoryChestStack[0]
                && !client.inventoryChestStack[1] && client.HasLockedInventory(),
                "deposit intent locks only captured nonempty slots, leaving empty pickup destinations alone");
            check(MENet.BeginInventoryDeposit(client, false) == null,
                "pending deposit suppresses duplicate local requests");
            client.inventory[2] = new Item(ModContent.ItemType<MEStorageUpgrade>());
            check(MENet.BeginStorageUpgrade(client, 2) == 0 && MENet.SlotAction(unit.Position, 0, false, false)
                && !MENet.CursorPending,
                "deposit excludes concurrent upgrade and cursor transactions");
            client.controlThrow = client.controlUseItem = client.controlUseTile = true;
            MENet.GuardInventoryTransactionInput(client);
            check(!client.controlThrow && !client.controlUseItem && !client.controlUseTile && client.noThrow > 0
                && !ItemLoader.CanStack(client.inventory[0], new Item(ItemID.Wood)),
                "deposit reuses throw, use and automatic stacking protections");
            check(!ItemLoader.CanPickup(new Item(ItemID.Wood), client),
                "pending deposit pauses world pickups until all authoritative replies arrive");
            if (!Main.dedServ)
                check(Terraria.GameInput.PlayerInput.IgnoreMouseInterface,
                    "pending deposit shares the native inventory mouse-action gate");
            // A newly filled slot not present at capture must not be deposited or overwritten.
            client.inventory[1] = new Item(ItemID.Gel, 7);
            Execute(intent);
            check(server.inventory[0].IsAir && unit.Ledger.GetAmount(wood) == 10,
                "real deposit packet handler debits the authoritative captured stack exactly once");
            Reply(intent);
            check(client.inventory[0].IsAir && Main.clientPlayer.inventory[0].IsAir
                && client.inventory[1].stack == 7 && !MENet.InventoryDepositPending && !client.HasLockedInventory(),
                "native deposit replies update local inventory and mirror without overwriting an uncaptured pickup");
            // Repeat the original duplication trigger using the real vanilla receivers.
            client.inventory[3] = client.inventory[0]; client.inventory[0] = new Item();
            Server(); ReceiveEquipment(0, 0, client.inventory[0]); ReceiveEquipment(0, 3, client.inventory[3]);
            long again = StorageOperations.DepositInventory(new() { unit }, server, onlySlot: 3);
            check(again == 0 && unit.Ledger.GetAmount(wood) == 10 && server.inventory[3].IsAir,
                "deposit conservation: ordinary move and redeposit cannot recreate the original stack");
            server.inventory[0] = new Item(ItemID.Wood, 5);
            Execute(intent);
            check(server.inventory[0].stack == 5 && unit.Ledger.GetAmount(wood) == 10,
                "replaying a completed nonce never deposits newly acquired stock from the old slot");

            Setup((0, new Item(ItemID.Wood, 20)));
            unit.Ledger.MaxBytes = 9;
            intent = MENet.BeginInventoryDeposit(client, false);
            Execute(intent); Reply(intent);
            check(unit.Ledger.GetAmount(wood) == 8 && server.inventory[0].stack == 12
                && client.inventory[0].stack == 12 && !client.HasLockedInventory(),
                "partial network capacity preserves and synchronizes the exact remainder");
            intent = MENet.BeginInventoryDeposit(client, false);
            Execute(intent); Reply(intent);
            check(client.inventory[0].stack == 12 && unit.Ledger.GetAmount(wood) == 8
                && !MENet.InventoryDepositPending && !client.HasLockedInventory(),
                "full capacity returns unchanged stock and releases the pending request");

            Setup((0, new Item(ItemID.Wood, 10)));
            intent = MENet.BeginInventoryDeposit(client, false);
            server.name = "DepositRegressionVisitor";
            Execute(intent); Reply(intent);
            check(unit.Ledger.StoredTotalAmount == 0 && client.inventory[0].stack == 10
                && !MENet.InventoryDepositPending && !client.HasLockedInventory(),
                "denied permission neither consumes stock nor leaves deposit slots locked");

            Setup((0, new Item(ItemID.Wood, 10)));
            intent = MENet.BeginInventoryDeposit(client, false);
            server.inventory[0].stack = 4;
            Execute(intent); Reply(intent);
            check(unit.Ledger.GetAmount(wood) == 4 && client.inventory[0].IsAir,
                "deposit request contains no quantity and uses the server's current stack");

            Setup((0, new Item(ItemID.Wood, 10)), (1, new Item(ItemID.Gel, 4)));
            intent = MENet.BeginInventoryDeposit(client, false);
            server.inventory[0] = new Item(ItemID.StoneBlock, 6);
            server.inventory[1].favorited = true;
            Execute(intent); Reply(intent);
            check(unit.Ledger.StoredTotalAmount == 0 && client.inventory[0].type == ItemID.StoneBlock
                && server.inventory[1].stack == 4,
                "server refuses changed identities and newly favorited candidates without moving other slots");

            var sword = new Item(ItemID.CopperShortsword); sword.Prefix(PrefixID.Legendary);
            var unloaded = ItemIO.Load(new TagCompound { ["mod"] = "AE2RegressionMissingMod", ["name"] = "DepositItem",
                ["stack"] = 1, ["data"] = new TagCompound { ["charge"] = 11 } });
            Setup((0, sword), (1, unloaded));
            intent = MENet.BeginInventoryDeposit(client, false);
            server.inventory[0] = new Item(ItemID.CopperShortsword);
            server.inventory[1] = ItemIO.Load(new TagCompound { ["mod"] = "AE2RegressionMissingMod", ["name"] = "DepositItem",
                ["stack"] = 1, ["data"] = new TagCompound { ["charge"] = 12 } });
            Execute(intent); Reply(intent);
            check(unit.Ledger.StoredTotalAmount == 0 && !server.inventory[0].IsAir && !server.inventory[1].IsAir,
                "deposit identity verification rejects changed prefixes and changed custom data of the same type");

            var favorite = new Item(ItemID.Wood, 9) { favorited = true };
            Setup((0, new Item(ItemID.Wood, 2)), (1, new Item(ItemID.Wood, 3)),
                (2, new Item(ItemID.Gel, 4)), (3, favorite), (50, new Item(ItemID.CopperCoin, 5)));
            intent = MENet.BeginInventoryDeposit(client, false, onlyType: ItemID.Wood);
            check(intent.Slots == 3, "same-type capture excludes favorites, unrelated types and extra inventory slots");
            Execute(intent); Reply(intent);
            check(unit.Ledger.GetAmount(wood) == 5 && client.inventory[2].stack == 4
                && client.inventory[3].stack == 9 && client.inventory[50].stack == 5,
                "same-type deposit follows the declared slot set and preserves the remaining inventory");
            intent = MENet.BeginInventoryDeposit(client, true);
            Execute(intent); Reply(intent);
            check(client.inventory[2].IsAir && client.inventory[50].IsAir && client.inventory[3].stack == 9,
                "explicit extended deposit includes extra slots and still protects favorites");

            Setup((0, new Item(ItemID.Wood, 10)));
            intent = MENet.BeginInventoryDeposit(client, false);
            byte[] truncated = Encode(unit.Position, intent)[..^1];
            Execute(intent, truncated); Reply(intent);
            check(unit.Ledger.StoredTotalAmount == 0 && client.inventory[0].stack == 10
                && !MENet.InventoryDepositPending && !client.HasLockedInventory(),
                "truncated identity manifest cannot partially mutate stock and still receives a completion");

            Setup((0, new Item(ItemID.Wood, 10)));
            intent = MENet.BeginInventoryDeposit(client, false);
            intent.Slots |= 1UL << 58;
            Execute(intent);
            intent.Slots &= ~(1UL << 58); Reply(intent);
            check(unit.Ledger.StoredTotalAmount == 0 && client.inventory[0].stack == 10,
                "deposit protocol rejects cursor and out-of-scope slot bits before changing stock");
            intent = MENet.BeginInventoryDeposit(client, false);
            Complete(intent.Nonce + 1);
            check(MENet.InventoryDepositPending && client.inventoryChestStack[0],
                "stale completion cannot release a different pending deposit");
            client.inventoryChestStack[4] = true;
            MENet.ResetInventoryDeposits();
            check(!MENet.InventoryDepositPending && !client.inventoryChestStack[0] && client.inventoryChestStack[4],
                "disconnect reset releases deposit-owned slots without clearing unrelated vanilla locks");
            client.inventoryChestStack[4] = false;

            Setup((0, new Item(ItemID.Wood, 10)), (1, new Item(ModContent.ItemType<MEStorageUpgrade>(), 2)));
            MENet.CursorPending = true;
            check(MENet.BeginInventoryDeposit(client, false) == null,
                "pending cursor transaction blocks a new inventory deposit");
            MENet.CursorPending = false;
            check(MENet.BeginStorageUpgrade(client, 1) != 0 && MENet.BeginInventoryDeposit(client, false) == null,
                "pending upgrade blocks a new inventory deposit");
            MENet.ResetStorageUpgrades();

            Setup((0, new Item(ItemID.Wood, 10)));
            Server();
            foreach (byte legacy in new[] { MENet.ClientDepositAll, MENet.ClientDepositScope })
            {
                using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
                writer.Write(legacy); writer.Write(unit.Position.X); writer.Write(unit.Position.Y);
                stream.Position = 0; using var reader = new BinaryReader(stream);
                MENet.HandlePayload(reader, 0);
            }
            check(unit.Ledger.StoredTotalAmount == 0 && server.inventory[0].stack == 10,
                "retired unacknowledged deposit messages cannot bypass the transaction protocol");
        }
        finally
        {
            MENet.ResetInventoryDeposits(); MENet.ResetStorageUpgrades(); MENet.CursorPending = false;
            Main.player[0] = oldPlayer; Main.clientPlayer = oldClientPlayer;
            Array.Copy(oldRemotes,Netplay.Clients,oldRemotes.Length);
            Array.Copy(oldBuffers,NetMessage.buffer,oldBuffers.Length);
            foreach (MessageBuffer buffer in temporaryBuffers)
            { buffer?.reader?.Dispose(); buffer?.writer?.Dispose(); }
            netIdField.SetValue(mod,oldNetId);
            netModsField.SetValue(null,oldNetMods);
            Main.myPlayer = oldMyPlayer;
            Main.ServerSideCharacter = oldServerSideCharacter; Main.netMode = oldMode;
        }
    }

    private static byte[] Encode(Point16 position, MENet.InventoryDepositRequest intent)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        MENet.WriteInventoryDeposit(writer, position, intent);
        return stream.ToArray();
    }
    private static void Complete(int nonce)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(MENet.ServerInventoryDepositComplete); writer.Write((short)0); writer.Write((short)0);
        writer.Write(nonce); stream.Position = 0;
        using var reader = new BinaryReader(stream); MENet.HandlePayload(reader, 256);
    }
    private static void ReceiveEquipment(int sender, int slot, Item item)
    {
        var message = new MessageBuffer { whoAmI = sender };
        using var stream = new MemoryStream(message.readBuffer, true);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)MessageID.SyncEquipment); writer.Write((byte)0); writer.Write((short)slot);
        ItemIO.Send(item, writer, true);
        message.GetData(0, (int)stream.Position, out _);
        message.reader?.Dispose();
    }
}
