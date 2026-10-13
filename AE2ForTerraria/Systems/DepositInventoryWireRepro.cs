using System;
using System.Collections.Generic;
using System.IO;
using AE2ForTerraria.Core;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Validation;

// Opt-in diagnostic draft, intentionally outside the mod compilation tree.
// Run only during the isolated pre-world regression suite, with no live peers.
// Uses the real storage operation, ItemIO codec and MessageBuffer.GetData in both
// directions. It does not claim to test custom-packet routing or a TCP connection.
public static class DepositInventoryWireRepro
{
    private sealed class Storage : IMEStorage
    {
        public StorageLedger Ledger { get; } = new(8, 8, int.MaxValue) { MaxBytes = 1024 };
        public bool Allows(ItemKey key) => true;
    }

    public static void Run(Action<bool, string> check)
    {
        if (!RegressionChecks.IsRunning)
            throw new InvalidOperationException("Deposit repro requires the isolated regression runner.");
        for (int i = 0; i < Main.maxPlayers; i++)
            if (Main.player[i]?.active == true)
                throw new InvalidOperationException("Deposit repro refuses to run with an active player.");

        int oldMode = Main.netMode, oldMyPlayer = Main.myPlayer;
        bool oldServerSideCharacter = Main.ServerSideCharacter;
        Player oldPlayer = Main.player[0], oldClientPlayer = Main.clientPlayer;
        RemoteClient oldRemote = Netplay.Clients[0];
        var server = new Player { whoAmI = 0 };
        var client = new Player { whoAmI = 0 };
        server.inventory[0] = new Item(ItemID.Wood, 10);
        client.inventory[0] = new Item(ItemID.Wood, 10);
        var storage = new Storage();
        var units = new List<IMEStorage> { storage };
        ItemKey key = ItemKey.FromItem(server.inventory[0]);
        try
        {
            // State 10 permits normal vanilla packets, but the isolated RemoteClient
            // has no connected socket. No real player data or files are touched.
            Netplay.Clients[0] = new RemoteClient { State = 10 };
            Main.ServerSideCharacter = false;
            Main.clientPlayer = new Player();
            Main.netMode = NetmodeID.Server;
            Main.myPlayer = 255;
            Main.player[0] = server;
            long first = StorageOperations.DepositInventory(units, server, onlySlot: 0);
            check(first == 10 && server.inventory[0].IsAir && storage.Ledger.GetAmount(key) == 10,
                "deposit repro: real server operation deposits and removes exactly the starting stack");

            // The existing sender does not lock any player slots. Replay the exact
            // SyncEquipment fields that DepositInventory sends for the depleted slot.
            Main.netMode = NetmodeID.MultiplayerClient;
            Main.myPlayer = 0;
            Main.player[0] = client;
            ReceiveEquipment(256, 0, server.inventory[0]);
            bool clientKeptStock = client.inventory[0].type == ItemID.Wood
                && client.inventory[0].stack == 10;
            check(clientKeptStock,
                "deposit repro baseline: actual client receiver ignores its own unlocked removal snapshot");

            // An ordinary drag to another inventory slot generates these two vanilla
            // inventory updates. Neither packet invents an item or quantity.
            client.inventory[1] = client.inventory[0];
            client.inventory[0] = new Item();
            Main.netMode = NetmodeID.Server;
            Main.myPlayer = 255;
            Main.player[0] = server;
            ReceiveEquipment(0, 0, client.inventory[0]);
            ReceiveEquipment(0, 1, client.inventory[1]);
            check(server.inventory[1].type == ItemID.Wood && server.inventory[1].stack == 10,
                "deposit repro baseline: actual server receiver accepts the stale stack after a normal move");
            long second = StorageOperations.DepositInventory(units, server, onlySlot: 1);
            check(second == 10 && server.inventory[1].IsAir && storage.Ledger.GetAmount(key) == 20,
                "deposit repro baseline: the same original stack reaches storage a second time");

            // This is deliberately RED on the current runtime. Keep the preceding
            // observations in the log rather than silently changing the expected value.
            check(storage.Ledger.GetAmount(key) == 10,
                "deposit conservation: moving and redepositing after a completed deposit must not create stock");
        }
        finally
        {
            Main.player[0] = oldPlayer;
            Main.clientPlayer = oldClientPlayer;
            Netplay.Clients[0] = oldRemote;
            Main.myPlayer = oldMyPlayer;
            Main.ServerSideCharacter = oldServerSideCharacter;
            Main.netMode = oldMode;
        }
    }

    private static void ReceiveEquipment(int sender, int slot, Item item)
    {
        var message = new MessageBuffer { whoAmI = sender };
        using var stream = new MemoryStream(message.readBuffer, true);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)MessageID.SyncEquipment);
        writer.Write((byte)0);
        writer.Write((short)slot);
        ItemIO.Send(item, writer, true);
        message.GetData(0, (int)stream.Position, out _);
        message.reader?.Dispose();
    }
}
