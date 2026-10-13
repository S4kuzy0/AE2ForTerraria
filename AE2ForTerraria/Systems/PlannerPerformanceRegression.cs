using System;
using System.Diagnostics;
using System.Globalization;
using AE2ForTerraria.Core;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

// Pure planning over the currently loaded recipe index. No crafting, player state or UI changes.
public static class PlannerPerformanceRegression
{
    public static void Run(Action<bool, string> check)
    {
        if (Environment.GetEnvironmentVariable("AE2_PLANNER_BENCHMARK") != "1") return;
        foreach (int entries in new[] { 0, 1000 })
        {
            var stock = new StorageLedger(1, 1, int.MaxValue);
            for (int type = 1; type <= entries; type++) stock.Restore(ItemKey.Crafted(type), 100);
            foreach (int type in new[] { ItemID.Torch, ItemID.TerraBlade })
            {
                var planner = StorageSystem.Planner;
                ItemKey target = ItemKey.Crafted(type);
                planner.Plan(stock, target, 1, useExistingTarget: false);
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                int solved = 0, limited = 0;
                for (int run = 0; run < 3; run++)
                {
                    CraftPlan plan = planner.Plan(stock, target, 1, useExistingTarget: false);
                    if (plan.FullySatisfied) solved++;
                    if (plan.Limited) limited++;
                }
                TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                AE2ForTerraria.Instance.Logger.Info("AE2 PLANNER BENCHMARK: entries=" + entries
                    + " target=" + type + " iterations=3 elapsedMs="
                    + elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)
                    + " allocatedBytes=" + allocated + " solved=" + solved + " limited=" + limited);
                check(stock.StoredTypeCount == entries && stock.StoredTotalAmount == entries * 100L,
                    "real recipe planning benchmark preserves stock for " + entries + " entries and target " + type);
            }
        }
    }
}
