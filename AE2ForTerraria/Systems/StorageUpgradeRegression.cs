using System;
using System.IO;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

/// <summary>Isolated operation and wire checks. Does not open a world or change any saved player.</summary>
public static class StorageUpgradeRegression
{
    public static void Run(Action<bool, string> check)
    {
        int previousMode = Main.netMode;
        try
        {
            Main.netMode = NetmodeID.SinglePlayer;
            var unit = new MEStorageUnitEntity();
            var player = new Player { selectedItem = 3 };
            player.inventory[3] = new Item(ModContent.ItemType<MEStorageUpgrade>(), 2);
            player.inventory[4] = new Item(ItemID.Wood, 17);
            var key = ItemKey.Crafted(ItemID.StoneBlock);
            unit.Ledger.Restore(key, 250);

            check(StorageOperations.TryUpgradeStorageUnit(unit, player)
                && unit.Level == 2 && unit.CurrentCapacity == 32768 && unit.Ledger.MaxBytes == 32768
                && player.inventory[3].stack == 1 && player.inventory[4].stack == 17
                && unit.Ledger.GetAmount(key) == 250,
                "storage upgrade changes capacity, consumes exactly one held item and preserves other contents");

            check(StorageOperations.TryUpgradeStorageUnit(unit, player)
                && unit.Level == 3 && unit.Ledger.MaxBytes == 131072 && player.inventory[3].IsAir,
                "final upgrade consumes the last item without leaving a zero-stack ghost");
            player.inventory[3] = new Item(ModContent.ItemType<MEStorageUpgrade>(), 2);
            check(!StorageOperations.TryUpgradeStorageUnit(unit, player)
                && unit.Level == 3 && player.inventory[3].stack == 2,
                "maximum-level upgrade refuses without consuming the upgrade item");

            var fresh = new MEStorageUnitEntity();
            player.inventory[3] = new Item(ItemID.Wood, 10);
            check(!StorageOperations.TryUpgradeStorageUnit(fresh, player)
                && fresh.Level == 1 && player.inventory[3].stack == 10,
                "wrong held item cannot upgrade or consume inventory");
            player.inventory[3] = new Item(ModContent.ItemType<MEStorageUpgrade>(), 1);
            player.inventory[3].stack = 0;
            check(!StorageOperations.TryUpgradeStorageUnit(fresh, player) && fresh.Level == 1,
                "zero-stack upgrade item cannot grant a free level");
            player.inventory[3] = new Item(ModContent.ItemType<MEStorageUpgrade>(), 1);
            Main.netMode = NetmodeID.MultiplayerClient;
            check(!StorageOperations.TryUpgradeStorageUnit(fresh, player)
                && fresh.Level == 1 && player.inventory[3].stack == 1,
                "client cannot perform the authoritative upgrade operation");
            Main.netMode = NetmodeID.Server;
            check(StorageOperations.TryUpgradeStorageUnit(fresh, player)
                && fresh.Level == 2 && player.inventory[3].IsAir,
                "server uses the same upgrade and consumption rules as single player");

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            unit.NetSend(writer);
            stream.Position = 0;
            var client = new MEStorageUnitEntity();
            client.Ledger.Restore(key, 19);
            client.Filter.Add(ItemID.StoneBlock);
            client.IsWhitelist = true;
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
            client.NetReceive(reader);
            check(client.Level == 3 && client.CurrentCapacity == unit.CurrentCapacity
                && client.Ledger.MaxBytes == unit.Ledger.MaxBytes && stream.Position == stream.Length,
                "storage unit metadata round trip restores level and capacity for a new client");
            check(client.Ledger.GetAmount(key) == 19 && client.IsWhitelist
                && client.Filter.Count == 1 && client.Filter[0] == ItemID.StoneBlock,
                "metadata updates the existing entity without replacing inventory or partition state");

            bool invalidRejected = Reject(client, new byte[] { 0 }) && Reject(client, new byte[] { 255 });
            check(invalidRejected && client.Level == 3 && client.Ledger.MaxBytes == 131072
                && client.Ledger.GetAmount(key) == 19,
                "invalid metadata levels preserve the previous entity state");
            check(Reject(client, Array.Empty<byte>()) && client.Level == 3,
                "truncated storage metadata is rejected without clearing the previous level");
            InventoryReplyChecks(check);
        }
        finally { Main.netMode = previousMode; }
    }

    private static void InventoryReplyChecks(Action<bool, string> check)
    {
        int oldMode = Main.netMode, oldPlayerId = Main.myPlayer;
        bool oldServerSideCharacter = Main.ServerSideCharacter;
        Player oldPlayer = Main.player[0], oldClientPlayer = Main.clientPlayer;
        var player = new Player { whoAmI = 0, selectedItem = 3 };
        player.inventory[3] = new Item(ModContent.ItemType<MEStorageUpgrade>(), 2);
        try
        {
            Main.netMode = NetmodeID.MultiplayerClient;
            Main.myPlayer = 0;
            Main.player[0] = player;
            Main.clientPlayer = new Player();
            Main.ServerSideCharacter = false;
            MENet.ResetStorageUpgrades();
            var remainder = new Item(ModContent.ItemType<MEStorageUpgrade>(), 1);
            ReceiveEquipment(3, remainder);
            check(player.inventory[3].stack == 2,
                "vanilla ignores an unlocked player's own equipment snapshot (upgrade regression baseline)");
            int request = MENet.BeginStorageUpgrade(player, 3);
            check(request != 0 && player.inventoryChestStack[3] && player.HasLockedInventory()
                && MENet.BeginStorageUpgrade(player, 3) == 0,
                "upgrade intent owns one vanilla slot lock and suppresses overlapping requests");
            player.controlThrow = player.controlUseItem = player.controlUseTile = true;
            MENet.GuardStorageUpgradeInput(player);
            check(!player.controlThrow && !player.controlUseItem && !player.controlUseTile && player.noThrow > 0,
                "pending upgrade prevents throw-key and world-use actions from bypassing the inventory slot lock");
            check(!ItemLoader.CanStack(player.inventory[3], remainder)
                && !ItemLoader.CanStack(remainder, player.inventory[3]),
                "pending upgrade item cannot gain or lose stock through automatic stacking");
            check(ItemLoader.CanStack(remainder, remainder.Clone()),
                "upgrade stack guard leaves unrelated stacks of the same item usable");
            if (!Main.dedServ)
            {
                check(Terraria.GameInput.PlayerInput.IgnoreMouseInterface,
                    "pending upgrade blocks vanilla inventory sorting and chest mouse actions at their shared gate");
                GamepadActionCheck(player, check);
            }
            player.selectedItem = 4;
            ReceiveEquipment(3, remainder);
            check(player.inventory[3].stack == 1 && Main.clientPlayer.inventory[3].stack == 1
                && !player.inventoryChestStack[3] && player.selectedItem == 4,
                "vanilla equipment receive applies the locked captured slot and updates its local mirror");
            MENet.CompleteStorageUpgrade(request + 1);
            check(MENet.StorageUpgradePending,
                "an unrelated completion cannot end the active upgrade request");
            player.inventoryChestStack[3] = true; // A later vanilla quick-stack now owns this flag.
            MENet.CompleteStorageUpgrade(request);
            check(!MENet.StorageUpgradePending && player.inventoryChestStack[3],
                "upgrade completion never unlocks a newer vanilla operation");
            player.inventoryChestStack[3] = false;
            request = MENet.BeginStorageUpgrade(player, 3);
            player.inventoryChestStack[4] = true;
            MENet.ResetStorageUpgrades();
            check(request != 0 && !player.inventoryChestStack[3] && player.inventoryChestStack[4],
                "reset releases the ME-owned slot without clearing unrelated slot locks");
            Main.netMode = NetmodeID.Server;
            player.inventory[4] = new Item(ModContent.ItemType<MEStorageUpgrade>(), 7);
            var unit = new MEStorageUnitEntity();
            check(StorageOperations.TryUpgradeStorageUnit(unit, player, 3)
                && player.inventory[3].IsAir && player.inventory[4].stack == 7,
                "server upgrade consumes the captured request slot even after selected item changes");
        }
        finally
        {
            MENet.ResetStorageUpgrades();
            Main.player[0] = oldPlayer;
            Main.clientPlayer = oldClientPlayer;
            Main.myPlayer = oldPlayerId;
            Main.ServerSideCharacter = oldServerSideCharacter;
            Main.netMode = oldMode;
        }
    }

    private static void ReceiveEquipment(int slot, Item item)
    {
        var message = new MessageBuffer { whoAmI = 256 };
        using var stream = new MemoryStream(message.readBuffer, true);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)MessageID.SyncEquipment);
        writer.Write((byte)Main.myPlayer);
        writer.Write((short)slot);
        ItemIO.Send(item, writer, true);
        message.GetData(0, (int)stream.Position, out _);
        message.reader?.Dispose();
    }

    private static void GamepadActionCheck(Player player, Action<bool, string> check)
    {
        // SmartCursor normally mutates favorited inside the same dispatcher that
        // executes equipment, transfer and throw shortcuts. Exercise that dispatcher
        // directly rather than testing its small, potentially inlined boolean getter.
        bool oldAllowed = Terraria.GameInput.PlayerInput.AllowExecutionOfGamepadInstructions;
        bool oldPressed = Terraria.GameInput.PlayerInput.Triggers.JustPressed.SmartCursor;
        bool oldFavorite = player.inventory[3].favorited;
        Item oldMouseItem = Main.mouseItem;
        try
        {
            Terraria.GameInput.PlayerInput.AllowExecutionOfGamepadInstructions = true;
            Terraria.GameInput.PlayerInput.Triggers.JustPressed.SmartCursor = true;
            player.inventory[3].favorited = false;
            Main.mouseItem = new Item();
            string instructions = Terraria.UI.ItemSlot.GetGamepadInstructions(player.inventory, 0, 3);
            check(instructions.Length == 0 && !player.inventory[3].favorited,
                "pending upgrade blocks the actual gamepad action dispatcher before inventory shortcuts execute");
        }
        finally
        {
            Terraria.GameInput.PlayerInput.AllowExecutionOfGamepadInstructions = oldAllowed;
            Terraria.GameInput.PlayerInput.Triggers.JustPressed.SmartCursor = oldPressed;
            player.inventory[3].favorited = oldFavorite;
            Main.mouseItem = oldMouseItem;
        }
    }

    private static bool Reject(MEStorageUnitEntity unit, byte[] bytes)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            unit.NetReceive(reader);
            return false;
        }
        catch (IOException) { return true; }
        catch (InvalidDataException) { return true; }
    }
}
