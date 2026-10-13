using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

/// <summary>Boundary, save and transaction checks for identity-limited disks; never loads a world.</summary>
public static class DiskCapacityRegression
{
    public static void Run(Action<bool, string> check)
    {
        int[] tiers = { ModContent.ItemType<MEDisk1K>(), ModContent.ItemType<MEDisk4K>(),
            ModContent.ItemType<MEDisk16K>(), ModContent.ItemType<MEDisk64K>() };
        long[] capacities = { 32, 64, 128, 256 };
        var wood = ItemKey.Crafted(ItemID.Wood);
        var stone = ItemKey.Crafted(ItemID.StoneBlock);
        var drive = new MEDiskDriveEntity();
        for (int i = 0; i < tiers.Length; i++)
        {
            drive.Disks[0] = new Item(tiers[i]); drive.RefreshCapacity();
            check(drive.Ledger.MaxTypes == capacities[i] && drive.Ledger.MaxBytes == long.MaxValue
                && drive.Ledger.MaxAmountPerType == int.MaxValue,
                "disk tier " + capacities[i] + " provides identity slots and an independent Int32 quantity limit");
        }
        drive.Disks[0] = new Item(tiers[0]); drive.Disks[1] = new Item(tiers[1]); drive.RefreshCapacity();
        check(drive.Ledger.MaxTypes == 96 && drive.Ledger.MaxAmountPerType == int.MaxValue,
            "mixed ordinary disks add identity slots without multiplying the per-identity limit");
        var ledger = drive.Ledger;
        check(ledger.Insert(wood, int.MaxValue) == int.MaxValue && ledger.Insert(stone, int.MaxValue) == int.MaxValue
            && ledger.StoredTotalAmount == 2L * int.MaxValue,
            "two item identities can each reach Int32 maximum without sharing an aggregate Int32 budget");
        check(ledger.Insert(wood, 1) == 0 && ledger.Insert(wood, 1, simulate: true) == 0,
            "both real and simulated insertion reject excess units of an already full identity");
        ledger.Extract(wood, 3); int revision = ledger.Version;
        check(ledger.Insert(wood, 9, simulate: true) == 3 && ledger.Version == revision
            && ledger.Insert(wood, 9) == 3,
            "partial insertion fills only the three available units and simulation does not mutate the ledger");
        var copy = ledger.Copy();
        check(copy.MaxAmountPerType == int.MaxValue && copy.Insert(wood, 1) == 0,
            "transaction copies retain the per-identity capacity");
        check(!StorageTransaction.TryApply(new List<IMEStorage> { drive },
                new Dictionary<ItemKey, long> { [stone] = 1 }, new Dictionary<ItemKey, long> { [wood] = 1 }, out _)
            && ledger.GetAmount(stone) == int.MaxValue && ledger.GetAmount(wood) == int.MaxValue,
            "crafting cannot bypass a full output identity or consume ingredients on a rejected commit");

        drive.Disks[1].TurnToAir(); drive.RefreshCapacity(); ledger.Clear();
        for (int i = 1; i <= 32; i++) ledger.Insert(ItemKey.Crafted(i), 1);
        check(ledger.StoredTypeCount == 32 && ledger.Insert(ItemKey.Crafted(33), 1) == 0
            && ledger.Insert(ItemKey.Crafted(1), 1) == 1,
            "full type slots reject only new identities while existing identities still accept items");
        ledger.Restore(ItemKey.Crafted(33), 5);
        check(ledger.StoredTypeCount == 33 && ledger.Insert(ItemKey.Crafted(1), 1) == 0
            && ledger.Extract(ItemKey.Crafted(33), 2) == 2,
            "legacy excess identities are retained and withdrawable while over-capacity drives reject deposits");
        for (int i = 34; i <= 80; i++) ledger.Restore(ItemKey.Crafted(i), 1);
        check(drive.CanReplaceDisk(1, new Item(tiers[0]), out _),
            "an over-capacity saved drive can add one disk even when a second disk is still needed");
        drive.Disks[1] = new Item(tiers[0]); drive.RefreshCapacity();
        check(!drive.CanRemoveDisk(1, out _), "removing capacity from an already over-capacity drive is rejected");
        var saved = new TagCompound(); drive.SaveData(saved);
        var restored = new MEDiskDriveEntity(); restored.LoadData(saved);
        check(restored.Ledger.StoredTypeCount == 80 && restored.Ledger.MaxTypes == 64
            && restored.Ledger.StoredTotalAmount == ledger.StoredTotalAmount,
            "save reload preserves all identities beyond the new type limit without truncation");
        ledger.Clear(); ledger.Restore(wood, (long)int.MaxValue + 17);
        saved = new TagCompound(); drive.SaveData(saved); restored.LoadData(saved);
        check(restored.Ledger.GetAmount(wood) == (long)int.MaxValue + 17
            && restored.Ledger.Insert(wood, 1) == 0 && restored.Ledger.Extract(wood, 17) == 17,
            "old quantities above Int32 maximum survive load and can be withdrawn without wrapping or clipping");

        drive.Disks[0] = new Item(ModContent.ItemType<MEDigitalSingularity>()); drive.RefreshCapacity();
        check(ledger.MaxTypes == long.MaxValue && ledger.MaxAmountPerType == long.MaxValue
            && ledger.Insert(wood, 1) == 1 && !drive.CanReplaceDisk(0, new Item(tiers[3]), out _),
            "digital singularity retains Int64 capacities and cannot be removed while its excess quantity is stored");
        CheckWire(check, ledger, wood);
        foreach (int hidden in new[] { ModContent.ItemType<MECraftingCpu>(), ModContent.ItemType<MEMolecularAssembler>(),
            ModContent.ItemType<MEStorageUnit>() })
        {
            var item = new Item(hidden);
            check(LegacyContentVisibility.IsHiddenFromCatalog(hidden) && item.ResearchUnlockCount == 0
                && !item.IsAir && item.createTile > 0
                && !Main.recipe.Take(Recipe.numRecipes).Any(r => r != null && !r.Disabled && r.createItem.type == hidden),
                "retired machine " + item.ModItem.Name + " remains loadable and placeable but leaves recipes, research and AE catalogues");
        }
        check(!LegacyContentVisibility.IsHiddenFromCatalog(tiers[0])
            && !LegacyContentVisibility.IsHiddenFromCatalog(ItemID.Wood),
            "legacy catalogue filtering leaves disks and unrelated items visible");
    }

    private static void CheckWire(Action<bool, string> check, StorageLedger source, ItemKey key)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        typeof(MENet).GetMethod("WriteLedger", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { writer, source });
        stream.Position = 0;
        var target = new StorageLedger();
        using var reader = new BinaryReader(stream);
        typeof(MENet).GetMethod("ReadLedgerInto", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { reader, target });
        check(target.MaxAmountPerType == source.MaxAmountPerType && target.MaxTypes == source.MaxTypes
            && target.GetAmount(key) == source.GetAmount(key) && stream.Position == stream.Length,
            "wire ledger round trip preserves per-identity capacity and exact stored quantities");
    }
}
