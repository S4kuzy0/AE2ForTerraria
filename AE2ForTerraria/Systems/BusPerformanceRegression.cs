using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Explicit, in-memory benchmark. Never runs unless both the regression runner and benchmark switch enable it.</summary>
public static class BusPerformanceRegression
{
    public static void Run(Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("AE2_BUS_BENCHMARK") != "1") return;
        using var world = new RegressionWorld();
        foreach (int entries in new[] { 100, 1000 })
        foreach (int buses in new[] { 1, 4 })
        {
            world.Reset();
            Chest chest = world.PlaceChest(40, 40);
            for (int slot = 0; slot < chest.item.Length; slot++)
            {
                var item = new Item(ItemID.DirtBlock);
                item.stack = item.maxStack;
                chest.item[slot] = item;
            }
            // The outer ring links every bus without crossing any chest tile.
            for (int offset = 38; offset <= 43; offset++)
            {
                world.SetTile(offset, 38, ModContent.TileType<MECableTile>());
                world.SetTile(offset, 43, ModContent.TileType<MECableTile>());
                world.SetTile(38, offset, ModContent.TileType<MECableTile>());
                world.SetTile(43, offset, ModContent.TileType<MECableTile>());
            }
            var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 38, 38);
            Point16[] positions = { new(39, 40), new(42, 40), new(40, 39), new(40, 42) };
            for (int i = 0; i < buses; i++)
            {
                var bus = world.Place(new MEStorageBusEntity(), ModContent.TileType<MEStorageBusTile>(), positions[i].X, positions[i].Y);
                bus.SetExportMode(true);
            }
            for (int type = 1; storage.Ledger.StoredTypeCount < entries; type++)
            {
                if (type == ItemID.DirtBlock) continue;
                if (type >= ItemID.Count) throw new InvalidOperationException("Insufficient vanilla item types for bus benchmark.");
                storage.Ledger.Restore(ItemKey.Crafted(type), 1);
            }
            ItemKey[] chestKeys = chest.item.Select(ItemKey.FromItem).ToArray();
            int[] chestAmounts = chest.item.Select(item => item.stack).ToArray();
            MENetwork.InvalidateCache();
            Measure(storage.Position, entries, buses, "topology-cold", 1);
            Measure(storage.Position, entries, buses, "topology-warm", 3);
            check(storage.Ledger.StoredTypeCount == entries && storage.Ledger.StoredTotalAmount == entries
                && storage.Ledger.Entries.All(pair => pair.Value == 1)
                && chest.item.Select((item, slot) => item.stack == chestAmounts[slot] && ItemKey.FromItem(item).Equals(chestKeys[slot])).All(value => value),
                $"bus benchmark leaves exact identities and quantities unchanged for {entries} entries and {buses} buses");
        }
    }

    private static void Measure(Point16 anchor, int entries, int buses, string phase, int repetitions)
    {
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        long moved = 0;
        for (int i = 0; i < repetitions; i++)
            moved += MEStorageBusEntity.ExportNetworkToChests(anchor.X, anchor.Y);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        AE2ForTerraria.Instance.Logger.Info("AE2 BUS BENCHMARK: entries=" + entries + " buses=" + buses
            + " phase=" + phase + " iterations=" + repetitions
            + " elapsedMs=" + elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)
            + " allocatedBytes=" + allocated + " moved=" + moved);
        if (moved != 0) throw new InvalidOperationException("Full unrelated chest unexpectedly accepted benchmark stock.");
    }
}
