using System;
using System.Linq;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Runs actual bus ticks and the public network export entry against a restored in-memory fixture.</summary>
public static class StorageBusRegression
{
    private static readonly Point16[] Contacts =
    {
        new(39, 40), new(39, 41), new(42, 40), new(42, 41),
        new(40, 39), new(41, 39), new(40, 42), new(41, 42)
    };

    private static (MEStorageBusEntity Bus, MEStorageUnitEntity Storage, Chest Chest) Setup(RegressionWorld world, Point16 position, int chestType = TileID.Containers)
    {
        world.Reset();
        Chest chest = world.PlaceChest(40, 40, chestType);
        var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), position.X, position.Y);
        int dx = position.X < 40 ? -1 : position.X > 41 ? 1 : 0;
        int dy = position.Y < 40 ? -1 : position.Y > 41 ? 1 : 0;
        if (dx != 0) dy = 0;
        if (dx == 0 && dy == 0) throw new InvalidOperationException("Fixture bus must be outside chest.");
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), position.X + dx, position.Y + dy);
        return (bus, storage, chest);
    }

    private static void Tick(MEStorageBusEntity bus)
    {
        for (int tick = 0; tick < 30; tick++) bus.Update();
    }

    public static void Run(Action<bool, string> check)
    {
        using var world = new RegressionWorld();
        ItemKey wood = ItemKey.Crafted(ItemID.Wood);
        foreach (int chestType in new[] { (int)TileID.Containers, TileID.Containers2 })
        foreach (Point16 contact in Contacts)
        {
            var (bus, storage, chest) = Setup(world, contact, chestType);
            chest.item[0] = new Item(ItemID.Wood, 7);
            Tick(bus);
            check(storage.Ledger.GetAmount(wood) == 7 && chest.item[0].IsAir,
                $"bus imports native chest {chestType} through contact {contact.X},{contact.Y}");
            bus.SetExportMode(true);
            Tick(bus);
            check(storage.Ledger.GetAmount(wood) == 0 && chest.item[0].type == ItemID.Wood && chest.item[0].stack == 7,
                $"bus exports native chest {chestType} through contact {contact.X},{contact.Y}");
            chest.item[0].TurnToAir(); storage.Ledger.Restore(wood, 7);
            long moved = MEStorageBusEntity.ExportNetworkToChests(bus.Position.X, bus.Position.Y);
            check(moved == 7 && storage.Ledger.GetAmount(wood) == 0 && chest.item[0].stack == 7,
                $"manual export uses the same native chest contact {chestType} at {contact.X},{contact.Y}");
        }

        // This same entry-point test fails on the original source: only (39,40) and (40,39) resolve.
        // To reproduce the red baseline, keep this file and replace only MEStorageBusEntity.cs with
        // the backed-up 0.23.1 file, then enable AE2_RUN_REGRESSION in the isolated test process.
        NegativeContacts(world, wood, check);
        CapacityAndPolicy(world, wood, check);
        ContainerVariantsAndIdentity(world, wood, check);
    }

    private static void NegativeContacts(RegressionWorld world, ItemKey wood, Action<bool, string> check)
    {
        foreach (Point16 position in new[] { new Point16(39, 39), new Point16(43, 40), new Point16(40, 43) })
        {
            var (bus, storage, chest) = Setup(world, position);
            chest.item[0] = new Item(ItemID.Wood, 7);
            Tick(bus);
            check(storage.Ledger.StoredTotalAmount == 0 && chest.item[0].stack == 7,
                $"bus never imports diagonal or separated chest {position.X},{position.Y}");
            bus.SetExportMode(true); storage.Ledger.Restore(wood, 3);
            check(MEStorageBusEntity.ExportNetworkToChests(position.X, position.Y) == 0
                && storage.Ledger.GetAmount(wood) == 3 && chest.item[0].stack == 7,
                $"manual export never reaches diagonal or separated chest {position.X},{position.Y}");
        }

        var fixture = Setup(world, new Point16(42, 40));
        fixture.Chest.item[0] = new Item(ItemID.Wood, 7);
        world.ClearTile(41, 40); // Stale chest record is not enough when the touching tile was removed.
        Tick(fixture.Bus);
        check(fixture.Storage.Ledger.StoredTotalAmount == 0 && fixture.Chest.item[0].stack == 7,
            "removed touching chest tile cannot be imported through a stale chest record");
        world.SetTile(41, 40, TileID.Stone);
        Tick(fixture.Bus);
        check(fixture.Storage.Ledger.StoredTotalAmount == 0 && fixture.Chest.item[0].stack == 7,
            "ordinary solid tile never connects a nearby native chest");
    }

    private static void CapacityAndPolicy(RegressionWorld world, ItemKey wood, Action<bool, string> check)
    {
        var (bus, storage, chest) = Setup(world, new Point16(42, 41));
        storage.Ledger.MaxBytes = 9;
        chest.item[0] = new Item(ItemID.Wood, 20);
        Tick(bus);
        check(storage.Ledger.GetAmount(wood) == 8 && chest.item[0].stack == 12,
            "new chest contact preserves remainder on partial import");
        Tick(bus);
        check(storage.Ledger.GetAmount(wood) == 8 && chest.item[0].stack == 12,
            "full network leaves native chest unchanged");

        storage.Ledger.Clear(); storage.Ledger.MaxBytes = 8192;
        bus.IsWhitelist = true; bus.Filter.Add(ItemID.StoneBlock); bus.MarkFilterDirty();
        Tick(bus);
        check(storage.Ledger.StoredTotalAmount == 0 && chest.item[0].stack == 12,
            "bus whitelist still applies after chest contact normalization");
        bus.Filter.Clear(); bus.MarkFilterDirty(); chest.item[0].TurnToAir();
        bus.SetExportMode(true); storage.Ledger.Restore(wood, 11);
        Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(bus.Position.X, bus.Position.Y));
        NetworkRegistry.TryClaim(anchor, "AE2RegressionOwner"); NetworkRegistry.SetLocked(anchor, ItemID.Wood, true);
        Tick(bus);
        check(storage.Ledger.GetAmount(wood) == 11 && chest.item.All(item => item.IsAir),
            "automatic export cannot move a locked item at a newly recognized contact");
        check(MEStorageBusEntity.ExportNetworkToChests(bus.Position.X, bus.Position.Y, "AE2RegressionVisitor") == 0,
            "manual export visitor cannot move locked item");
        check(MEStorageBusEntity.ExportNetworkToChests(bus.Position.X, bus.Position.Y, "AE2RegressionOwner") == 11
            && storage.Ledger.GetAmount(wood) == 0 && chest.item[0].stack == 11,
            "manual export owner can move locked item without losing quantity");

        NetworkRegistry.SetLocked(anchor, ItemID.Wood, false);
        for (int slot = 0; slot < chest.item.Length; slot++) chest.item[slot] = new Item(ItemID.StoneBlock, 9999);
        chest.item[0] = new Item(ItemID.Wood, new Item(ItemID.Wood).maxStack - 2);
        storage.Ledger.Restore(wood, 10);
        Tick(bus);
        check(storage.Ledger.GetAmount(wood) == 8 && chest.item[0].stack == chest.item[0].maxStack,
            "partially full native chest only debits the accepted amount");
        Tick(bus);
        check(storage.Ledger.GetAmount(wood) == 8,
            "full native chest does not debit storage");
    }

    private static void ContainerVariantsAndIdentity(RegressionWorld world, ItemKey wood, Action<bool, string> check)
    {
        world.Reset();
        Chest dresser = world.PlaceChest(40, 40, TileID.Dressers, 1);
        var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), 43, 41);
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 44, 41);
        dresser.item[0] = new Item(ItemID.Wood, 4);
        Tick(bus);
        check(storage.Ledger.GetAmount(wood) == 4 && dresser.item[0].IsAir,
            "native dresser right lower contact resolves its three-wide origin");

        var fixture = Setup(world, new Point16(42, 40));
        Tick(fixture.Bus);
        check(fixture.Storage.Ledger.StoredTotalAmount == 0 && fixture.Chest.item.All(item => item.IsAir),
            "empty connected native chest leaves storage unchanged");
        var sword = new Item(ItemID.CopperShortsword); sword.Prefix(PrefixID.Legendary);
        ItemKey swordKey = ItemKey.FromItem(sword);
        fixture.Chest.item[3] = sword;
        Tick(fixture.Bus); fixture.Bus.SetExportMode(true); Tick(fixture.Bus);
        Item restored = fixture.Chest.item.Single(item => !item.IsAir);
        check(fixture.Storage.Ledger.StoredTotalAmount == 0 && ItemKey.FromItem(restored).Equals(swordKey)
            && restored.stack == 1, "chest import/export retains exact prefixed item identity");

        world.Reset();
        Chest right = world.PlaceChest(41, 40);
        Chest left = world.PlaceChest(38, 40);
        bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), 40, 40);
        storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 40, 39);
        right.item[0] = new Item(ItemID.Wood, 3); left.item[0] = new Item(ItemID.Wood, 5);
        Tick(bus);
        check(storage.Ledger.GetAmount(wood) == 8 && right.item[0].IsAir && left.item[0].IsAir,
            "bus handles two genuinely adjacent chests without dropping or double counting stock");
    }
}
