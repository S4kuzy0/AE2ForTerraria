using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

/// <summary>Exercises real long-quantity storage, disk operations and ItemIO without saving a world or player.</summary>
public static class DigitalSingularityRegression
{
    public static void Run(Action<bool, string> check)
    {
        ItemKey wood = ItemKey.Crafted(ItemID.Wood), stone = ItemKey.Crafted(ItemID.StoneBlock);
        int singularity = ModContent.ItemType<MEDigitalSingularity>();
        int ordinary = ModContent.ItemType<MEDisk64K>();
        var drive = new MEDiskDriveEntity();
        drive.Disks[0] = new Item(singularity);
        drive.Disks[1] = new Item(ordinary);
        drive.RefreshCapacity();
        check(drive.Ledger.MaxBytes == long.MaxValue && drive.Ledger.MaxTypes == long.MaxValue,
            "singularity mixed with ordinary disk keeps both logical capacities at Int64 maximum");
        for (int i = 0; i < MEDiskDriveEntity.DiskSlots; i++) drive.Disks[i] = new Item(singularity);
        drive.RefreshCapacity();
        check(drive.Ledger.MaxBytes == long.MaxValue && drive.Ledger.MaxTypes == long.MaxValue,
            "ten singularity disks never wrap byte or identity capacity");

        var ledger = drive.Ledger;
        ledger.Restore(wood, long.MaxValue - 2);
        int revision = ledger.Version;
        check(ledger.Insert(stone, 10, simulate: true) == 2 && ledger.Version == revision && ledger.StoredTypeCount == 1,
            "quantity-boundary simulation reports partial fit without changing identities or revision");
        check(ledger.Insert(stone, 10) == 2 && ledger.GetAmount(wood) == long.MaxValue - 2
            && ledger.GetAmount(stone) == 2 && ledger.StoredTotalAmount == long.MaxValue,
            "last two units fit exactly and leave excess with caller at Int64 total limit");
        check(ledger.Insert(wood, 1) == 0 && ledger.Insert(ItemKey.Crafted(ItemID.Gel), 1) == 0
            && ledger.StoredTypeCount == 2 && ledger.UsedBytes > 0,
            "full singularity refuses both existing and new identities without negative byte usage");
        check(ledger.Extract(stone, 1) == 1 && ledger.Insert(wood, 3) == 1
            && ledger.StoredTotalAmount == long.MaxValue && ledger.GetAmount(stone) == 1,
            "withdrawing one unit frees exactly one unit at the total quantity boundary");
        revision = ledger.Version;
        bool rejected = false;
        try { ledger.Restore(wood, 1); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected && ledger.Version == revision && ledger.StoredTotalAmount == long.MaxValue,
            "authoritative restore rejects overflow before mutating existing stock");

        var storage = new List<IMEStorage> { drive };
        var consumed = new Dictionary<ItemKey, long> { [wood] = 1 };
        var produced = new Dictionary<ItemKey, long> { [stone] = 2 };
        long beforeWood = ledger.GetAmount(wood), beforeStone = ledger.GetAmount(stone);
        check(!StorageTransaction.TryApply(storage, consumed, produced, out _)
            && ledger.GetAmount(wood) == beforeWood && ledger.GetAmount(stone) == beforeStone,
            "craft output exceeding Int64 total capacity leaves all source quantities intact");
        produced[stone] = 1;
        check(StorageTransaction.TryApply(storage, consumed, produced, out _)
            && ledger.StoredTotalAmount == long.MaxValue && ledger.GetAmount(wood) == beforeWood - 1
            && ledger.GetAmount(stone) == beforeStone + 1,
            "equal-volume craft commits at the maximum total without losing its output");

        for (int i = 1; i < MEDiskDriveEntity.DiskSlots; i++) drive.Disks[i] = new Item();
        drive.RefreshCapacity();
        Item cursor = new Item(ordinary);
        Item originalCursor = cursor, originalDisk = drive.Disks[0];
        check(!ContainerSlotOperations.TryChange(drive.Disks, 0, ref cursor, false, drive, out string reason)
            && !string.IsNullOrEmpty(reason) && ReferenceEquals(cursor, originalCursor)
            && ReferenceEquals(drive.Disks[0], originalDisk) && ledger.StoredTotalAmount == long.MaxValue,
            "replacing the last full singularity with a finite disk preserves cursor, slot and ledger");
        drive.Disks[1] = new Item(singularity);
        drive.RefreshCapacity();
        check(ContainerSlotOperations.TryChange(drive.Disks, 0, ref cursor, false, drive, out _)
            && cursor.type == singularity && drive.Disks[0].type == ordinary
            && ledger.MaxBytes == long.MaxValue && ledger.StoredTotalAmount == long.MaxValue,
            "direct disk replacement succeeds when another singularity still covers the full ledger");

        var saved = new TagCompound();
        drive.SaveData(saved);
        using var stream = new MemoryStream();
        TagIO.ToStream(saved, stream, false);
        stream.Position = 0;
        var restored = new MEDiskDriveEntity();
        restored.LoadData(TagIO.FromStream(stream, false));
        check(restored.Ledger.StoredTotalAmount == long.MaxValue && restored.Ledger.MaxTypes == long.MaxValue
            && restored.Ledger.GetAmount(wood) == ledger.GetAmount(wood)
            && restored.Ledger.GetAmount(stone) == ledger.GetAmount(stone)
            && restored.Disks[0].type == ordinary && restored.Disks[1].type == singularity,
            "drive ItemIO and TagIO round trip preserves mixed disks and exact Int64 quantities");

        var oldEntry = new TagCompound { ["amount"] = 37 };
        ItemPersistence.WriteKey(oldEntry, wood);
        var oldSave = new TagCompound { ["entries"] = new List<TagCompound> { oldEntry } };
        var oldLedger = new StorageLedger();
        ItemPersistence.LoadLedger(oldLedger, oldSave);
        check(oldLedger.GetAmount(wood) == 37,
            "existing Int32 saved quantity remains readable after long identity-capacity migration");
        var finite = new MEDiskDriveEntity(); finite.Disks[0] = new Item(ordinary); finite.RefreshCapacity();
        var unit = new MEStorageUnitEntity();
        check(finite.Ledger.MaxBytes == long.MaxValue && finite.Ledger.MaxTypes == 256
            && finite.Ledger.MaxAmountPerType == int.MaxValue
            && unit.Ledger.MaxTypes == 24 && unit.Ledger.MaxBytes == 8192,
            "ordinary disks use type slots and per-identity limits while legacy storage units retain their capacity rules");

        var first = new MEDiskDriveEntity(); first.Ledger.Restore(wood, long.MaxValue);
        var second = new MEDiskDriveEntity(); second.Ledger.Restore(wood, 7); second.Ledger.Restore(stone, long.MaxValue - 7);
        NetworkInventory.Reset();
        try
        {
            var network = new List<IMEStorage> { first, second };
            var contents = NetworkInventory.Get(network);
            check(contents[wood] == long.MaxValue && contents[stone] == long.MaxValue - 7
                && first.Ledger.GetAmount(wood) == long.MaxValue && second.Ledger.GetAmount(wood) == 7,
                "multi-drive summary saturates individual identities without altering authoritative stock");
            check(StorageAmounts.Sum(contents.Values) == long.MaxValue,
                "multi-identity network quantity summary never wraps negative");
            check(ReferenceEquals(contents, NetworkInventory.Get(network)),
                "large-quantity inventory summary still reuses an unchanged snapshot");
            first.Ledger.Extract(wood, 8);
            var changed = NetworkInventory.Get(network);
            check(!ReferenceEquals(contents, changed) && changed[wood] == long.MaxValue - 1,
                "large-quantity summary invalidates after a real withdrawal and resumes exact counts below saturation");
        }
        finally { NetworkInventory.Reset(); }

        check(UIFormat.Exact(long.MaxValue) == "9,223,372,036,854,775,807" && UIFormat.Count(long.MaxValue).Length <= 6,
            "maximum quantity has compact grid text and an exact unrounded detail representation");
        bool recipeFound = Main.recipe.Take(Recipe.numRecipes).Any(recipe => recipe != null
            && recipe.createItem.type == singularity
            && recipe.requiredItem.Count(item => !item.IsAir) == 1
            && recipe.requiredItem.Any(item => item.type == ItemID.LunarBar && item.stack == 8)
            && recipe.requiredTile.Contains(TileID.LunarCraftingStation));
        check(recipeFound, "digital singularity recipe uses eight Luminite Bars at the Ancient Manipulator");
        AtomicLoadChecks(check, singularity, ordinary, wood, stone);
        TransferChecks(check, singularity, wood, stone);
    }

    private static void AtomicLoadChecks(Action<bool, string> check, int singularity, int ordinary, ItemKey wood, ItemKey stone)
    {
        var first = new TagCompound { ["amount"] = long.MaxValue };
        var invalidLater = new TagCompound { ["amount"] = 1L };
        ItemPersistence.WriteKey(first, wood); ItemPersistence.WriteKey(invalidLater, stone);
        var corrupt = new TagCompound
        {
            ["entries"] = new List<TagCompound> { first, invalidLater },
            ["disks"] = new List<Item> { new Item(singularity) },
            ["level"] = 3, ["whitelist"] = false
        };
        ItemPersistence.SaveFilter(corrupt, "filter", new List<int> { ItemID.StoneBlock });
        bool Rejects(Action load)
        {
            try { load(); return false; }
            catch (InvalidDataException) { return true; }
        }
        var ledger = new StorageLedger(); ledger.Restore(wood, 23);
        int revision = ledger.Version;
        check(Rejects(() => ItemPersistence.LoadLedger(ledger, corrupt)) && ledger.GetAmount(wood) == 23
            && ledger.GetAmount(stone) == 0 && ledger.Version == revision,
            "overflow in a later saved entry rejects the complete read without clearing or partially replacing existing stock");

        var drive = new MEDiskDriveEntity();
        drive.Disks[0] = new Item(ordinary); drive.RefreshCapacity(); drive.Ledger.Restore(wood, 23);
        drive.Filter = new List<int> { ItemID.Wood }; drive.IsWhitelist = true;
        Item oldDisk = drive.Disks[0]; var oldFilter = drive.Filter; revision = drive.Ledger.Version;
        check(Rejects(() => drive.LoadData(corrupt)) && drive.Ledger.GetAmount(wood) == 23
            && drive.Ledger.Version == revision && drive.Ledger.MaxBytes == long.MaxValue
            && drive.Ledger.MaxTypes == 256 && drive.Ledger.MaxAmountPerType == int.MaxValue
            && ReferenceEquals(drive.Disks[0], oldDisk) && ReferenceEquals(drive.Filter, oldFilter) && drive.IsWhitelist,
            "failed drive reload preserves previous disks, capacity, filter and complete authoritative ledger");

        var unit = new MEStorageUnitEntity(); unit.TryUpgrade(); unit.Ledger.Restore(wood, 23);
        unit.Filter = new List<int> { ItemID.Wood }; unit.IsWhitelist = true;
        oldFilter = unit.Filter; revision = unit.Ledger.Version;
        check(Rejects(() => unit.LoadData(corrupt)) && unit.Level == 2 && unit.CurrentCapacity == 32768
            && unit.Ledger.GetAmount(wood) == 23 && unit.Ledger.Version == revision
            && ReferenceEquals(unit.Filter, oldFilter) && unit.IsWhitelist,
            "failed storage-unit reload preserves its existing level, capacity, filter and authoritative ledger");

        var newDrive = new MEDiskDriveEntity();
        check(Rejects(() => newDrive.LoadData(corrupt)) && newDrive.Ledger.StoredTotalAmount == 0
            && newDrive.Disks.All(item => item.IsAir),
            "a new drive explicitly fails a corrupt save load and does not publish partial contents or disks");
        revision = ledger.Version;
        ItemPersistence.LoadLedger(ledger, new TagCompound());
        check(ledger.StoredTotalAmount == 0 && ledger.Version != revision,
            "a successfully decoded empty save intentionally replaces previous ledger contents");
    }

    private static void TransferChecks(Action<bool, string> check, int singularity, ItemKey wood, ItemKey stone)
    {
        using var world = new RegressionWorld();
        MEDiskDriveEntity Drive(int x, int y, ItemKey key, long quantity)
        {
            var result = world.Place(new MEDiskDriveEntity(), ModContent.TileType<MEDiskDriveTile>(), x, y);
            result.Disks[0] = new Item(singularity); result.RefreshCapacity();
            if (quantity > 0) result.Ledger.Restore(key, quantity);
            return result;
        }
        var first = Drive(40, 40, wood, long.MaxValue);
        var second = Drive(41, 40, stone, long.MaxValue);
        var targetFirst = Drive(50, 40, wood, 0);
        var targetSecond = Drive(51, 40, stone, 0);
        long reported = MENetwork.TransferBetweenNetworks(40, 40, 50, 40);
        check(reported == long.MaxValue && first.Ledger.StoredTotalAmount == 0 && second.Ledger.StoredTotalAmount == 0
            && StorageAmounts.Sum(new[] { targetFirst.Ledger.GetAmount(wood), targetSecond.Ledger.GetAmount(wood) }) == long.MaxValue
            && StorageAmounts.Sum(new[] { targetFirst.Ledger.GetAmount(stone), targetSecond.Ledger.GetAmount(stone) }) == long.MaxValue
            && targetFirst.Ledger.StoredTotalAmount == long.MaxValue && targetSecond.Ledger.StoredTotalAmount == long.MaxValue,
            "cross-network transfer conserves two full ledgers while its bounded reported total cannot wrap");

        world.Reset();
        Chest chest = world.PlaceChest(40, 40);
        var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), 42, 40);
        bus.SetExportMode(true);
        first = Drive(43, 40, wood, long.MaxValue);
        second = Drive(44, 40, stone, long.MaxValue);
        int maxStack = SavedItemGuard.MaxStackOf(ItemID.Wood);
        for (int slot = 0; slot < chest.item.Length; slot++) chest.item[slot] = new Item(ItemID.Wood, maxStack);
        chest.item[0].stack -= 3;
        reported = MEStorageBusEntity.ExportNetworkToChests(43, 40);
        check(reported == 3 && first.Ledger.GetAmount(wood) == long.MaxValue - 3
            && second.Ledger.GetAmount(stone) == long.MaxValue && chest.item[0].stack == maxStack,
            "bus export reports the actual three accepted items from a network whose total exceeds Int64 maximum");
        check(MEStorageBusEntity.ExportNetworkToChests(43, 40) == 0 && first.Ledger.GetAmount(wood) == long.MaxValue - 3,
            "full chest leaves all remaining singularity stock in its original ledger");
    }
}
