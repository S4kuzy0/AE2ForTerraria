using System;
using System.Diagnostics;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

// Stage-two draft: runs real queue operations and assembler ticks; never injects private active-job references.
public static class MultiCpuRegression
{
    private static readonly ItemKey Wood = ItemKey.Crafted(ItemID.Wood);
    private static readonly ItemKey Torch = ItemKey.Crafted(ItemID.Torch);
    private static readonly ItemKey Stone = ItemKey.Crafted(ItemID.StoneBlock);

    private static (MEMolecularAssemblerEntity Worker, MEStorageUnitEntity Storage,
        MECraftingCpuEntity First, MECraftingCpuEntity Second) Setup(RegressionWorld world, int wood = 20)
    {
        world.Reset();
        var worker = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 40, 40);
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 40, 41);
        var first = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 41, 40);
        var second = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 42, 40);
        storage.Ledger.Restore(Wood, wood);
        return (worker, storage, first, second);
    }

    private static void Tick(MEMolecularAssemblerEntity worker, int count)
    {
        for (int tick = 0; tick < count; tick++) worker.Update();
    }

    public static void Run(Action<bool, string> check)
    {
        Recipe[] originalRecipes = Main.recipe;
        int originalCount = Recipe.numRecipes;
        int originalSpeed = MEConfig.Instance.CraftTicksPerItem;
        bool originalDay = Main.dayTime;
        try
        {
            MEConfig.Instance.CraftTicksPerItem = 40;
            Main.recipe = new[]
            {
                Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood),
                Recipe.Create(ItemID.StoneBlock).AddIngredient(ItemID.Gel)
            };
            Recipe.numRecipes = Main.recipe.Length;
            StorageSystem.Recipes.Build();
            using var world = new RegressionWorld();
            BypassUnavailableCpu(world, check);
            MergePreexistingQueues(world, check);
            CancelPauseAndDisconnect(world, check);
            WorkersConserveQueue(world, check);
            QueueOrderAndFairness(world, check);
            ContinueBoundedScan(world, check);
            ContinueAfterWallClockYield(world, check);
            RetryAfterConditionChange(world, check);
        }
        finally
        {
            Main.recipe = originalRecipes; Recipe.numRecipes = originalCount;
            StorageSystem.Recipes.Build(); MEConfig.Instance.CraftTicksPerItem = originalSpeed;
            Main.dayTime = originalDay;
        }
    }

    private static void BypassUnavailableCpu(RegressionWorld world, Action<bool, string> check)
    {
        foreach (string state in new[] { "empty", "paused", "missing materials" })
        {
            var (worker, storage, first, second) = Setup(world);
            if (state == "paused") { first.Enqueue(Torch, 3); first.Paused = true; }
            if (state == "missing materials") first.Enqueue(Stone, 3);
            second.Enqueue(Torch, 1);
            var job = second.Jobs[0];
            Tick(worker, 10);
            if (worker.ActiveKey.IsAir) Tick(worker, 10); // A failed first plan may consume this selection's budget.
            check(worker.ActiveKey.Equals(Torch) && ReferenceEquals(second.ActiveJob, job) && first.ActiveJob == null,
                "later CPU starts while first CPU is " + state);
            Tick(worker, 45);
            check(second.Jobs.Count == 0 && storage.Ledger.GetAmount(Torch) == 1
                && storage.Ledger.GetAmount(Wood) == 19 && (first.Jobs.Count == 0 || first.Jobs[0].Remaining == 3),
                "later CPU commits only its own order while first CPU is " + state);
        }

        var fixture = Setup(world);
        Tick(fixture.Worker, 10);
        fixture.Second.Enqueue(Torch, 1);
        Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.Equals(Torch) && fixture.Second.ActiveJob != null,
            "new task on later CPU invalidates failed-selection signature");

        fixture = Setup(world);
        fixture.First.Enqueue(Stone, 1); fixture.First.Paused = true;
        fixture.Second.Enqueue(Torch, 1); fixture.Second.Paused = true;
        Tick(fixture.Worker, 10);
        fixture.Second.Paused = false;
        Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.Equals(Torch) && fixture.Second.ActiveJob != null,
            "unpausing a later CPU invalidates failed-selection signature");
    }

    private static void CancelPauseAndDisconnect(RegressionWorld world, Action<bool, string> check)
    {
        var fixture = Setup(world);
        fixture.Second.Enqueue(Torch, 2);
        var job = fixture.Second.Jobs[0];
        Tick(fixture.Worker, 10);
        fixture.Second.DropJob(job);
        Tick(fixture.Worker, 50);
        check(fixture.Worker.ActiveKey.IsAir && fixture.Second.ActiveJob == null
            && fixture.Storage.Ledger.GetAmount(Torch) == 0 && fixture.Storage.Ledger.GetAmount(Wood) == 20,
            "canceling active later-CPU task clears its marker without consuming any materials");

        fixture = Setup(world);
        fixture.Second.Enqueue(Torch, 2); job = fixture.Second.Jobs[0];
        Tick(fixture.Worker, 10);
        fixture.Second.Paused = true; fixture.Worker.Update();
        check(fixture.Worker.ActiveKey.IsAir && fixture.Second.ActiveJob == null && job.Remaining == 2,
            "pausing actual owning CPU clears active worker and correct CPU marker");
        fixture.Second.Paused = false; Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.Equals(Torch) && ReferenceEquals(fixture.Second.ActiveJob, job),
            "paused later CPU resumes through normal selection");

        world.ClearTile(fixture.Second.Position.X, fixture.Second.Position.Y);
        fixture.Worker.Update();
        check(fixture.Worker.ActiveKey.IsAir && fixture.Second.ActiveJob == null && job.Remaining == 2
            && fixture.Storage.Ledger.GetAmount(Wood) == 20,
            "disconnecting owning CPU cancels pending work without debiting its remaining order");
        world.SetTile(fixture.Second.Position.X, fixture.Second.Position.Y, ModContent.TileType<MECraftingCpuTile>());
        Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.Equals(Torch) && ReferenceEquals(fixture.Second.ActiveJob, job),
            "reconnecting original CPU allows its preserved queue to resume");

        MECraftingCpuEntity oldCpu = fixture.Second;
        TileEntity.ByID.Remove(oldCpu.ID); TileEntity.ByPosition.Remove(oldCpu.Position);
        var replacement = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), oldCpu.Position.X, oldCpu.Position.Y);
        Tick(fixture.Worker, 50);
        check(fixture.Worker.ActiveKey.IsAir && replacement.ActiveJob == null && oldCpu.ActiveJob == null
            && job.Remaining == 2 && fixture.Storage.Ledger.GetAmount(Wood) == 20,
            "new CPU at same coordinates never inherits an old object's pending job");
    }

    private static void MergePreexistingQueues(RegressionWorld world, Action<bool, string> check)
    {
        var fixture = Setup(world);
        TileEntity.ByID.Remove(fixture.Second.ID); TileEntity.ByPosition.Remove(fixture.Second.Position);
        world.ClearTile(fixture.Second.Position.X, fixture.Second.Position.Y);
        var distant = world.Place(fixture.Second, ModContent.TileType<MECraftingCpuTile>(), 45, 40);
        world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 46, 40);
        fixture.First.Enqueue(Stone, 1); fixture.First.Paused = true;
        distant.Enqueue(Torch, 1);
        MENetwork.Collect(distant.Position.X, distant.Position.Y);
        Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.IsAir && distant.Jobs.Count == 1,
            "disconnected queued CPU is not selected before a physical network merge");
        for (int x = 42; x <= 44; x++) world.SetTile(x, 40, ModContent.TileType<MECableTile>());
        Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.Equals(Torch) && ReferenceEquals(distant.ActiveJob, distant.Jobs[0]),
            "merging a second queued network exposes its runnable CPU despite a paused first CPU");
        Tick(fixture.Worker, 40);
        check(distant.Jobs.Count == 0 && fixture.First.Jobs[0].Remaining == 1 && fixture.First.Paused
            && fixture.Storage.Ledger.GetAmount(Torch) == 1,
            "merged networks retain first queue and complete only selected later CPU work");
    }

    private static void WorkersConserveQueue(RegressionWorld world, Action<bool, string> check)
    {
        var fixture = Setup(world);
        var other = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 40, 39);
        fixture.Second.Enqueue(Torch, 1);
        for (int tick = 0; tick < 70; tick++) { fixture.Worker.Update(); other.Update(); }
        check(fixture.Second.Jobs.Count == 0 && fixture.Second.ActiveJob == null
            && fixture.Storage.Ledger.GetAmount(Torch) == 1 && fixture.Storage.Ledger.GetAmount(Wood) == 19,
            "two physical workers cannot settle the same single remaining item twice");

        fixture = Setup(world);
        other = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 40, 39);
        fixture.Second.Enqueue(Torch, 2);
        var job = fixture.Second.Jobs[0];
        Tick(fixture.Worker, 30); Tick(other, 10); Tick(fixture.Worker, 19);
        check(fixture.Storage.Ledger.GetAmount(Torch) == 1 && job.Remaining == 1
            && ReferenceEquals(fixture.Second.ActiveJob, job) && other.ActiveKey.Equals(Torch),
            "worker completion retains owning CPU marker while another worker still runs that job");
        Tick(other, 45);
        check(fixture.Second.Jobs.Count == 0 && fixture.Second.ActiveJob == null
            && fixture.Storage.Ledger.GetAmount(Torch) == 2 && fixture.Storage.Ledger.GetAmount(Wood) == 18,
            "staggered workers settle exact remaining count and clear marker after final output");

        Main.recipe[0] = Recipe.Create(ItemID.Torch, 3).AddIngredient(ItemID.Wood);
        StorageSystem.Recipes.Build();
        fixture = Setup(world);
        other = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 40, 39);
        fixture.Second.Enqueue(Torch, 1);
        for (int tick = 0; tick < 70; tick++) { fixture.Worker.Update(); other.Update(); }
        check(fixture.Second.Jobs.Count == 0 && fixture.Storage.Ledger.GetAmount(Torch) == 3
            && fixture.Storage.Ledger.GetAmount(Wood) == 19,
            "parallel workers preserve one recipe batch surplus without producing a duplicate batch");
        Main.recipe[0] = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood);
        StorageSystem.Recipes.Build();
    }

    private static void QueueOrderAndFairness(RegressionWorld world, Action<bool, string> check)
    {
        var fixture = Setup(world);
        fixture.First.Enqueue(Torch, 5); fixture.Second.Enqueue(Torch, 5);
        Tick(fixture.Worker, 100);
        check(fixture.First.Jobs[0].Remaining == 4 && fixture.Second.Jobs[0].Remaining == 4
            && fixture.Storage.Ledger.GetAmount(Torch) == 2 && fixture.Storage.Ledger.GetAmount(Wood) == 18,
            "successive batches rotate between runnable CPUs instead of starving the later queue");

        fixture = Setup(world);
        fixture.Storage.Ledger.Restore(ItemKey.Crafted(ItemID.Gel), 1);
        fixture.Second.Enqueue(Torch, 1); fixture.Second.Enqueue(Stone, 1);
        var firstJob = fixture.Second.Jobs[0]; var secondJob = fixture.Second.Jobs[1];
        Tick(fixture.Worker, 10);
        check(ReferenceEquals(fixture.Second.ActiveJob, firstJob), "CPU selection retains local queue order");
        Tick(fixture.Worker, 49);
        check(ReferenceEquals(fixture.Second.ActiveJob, secondJob) && fixture.Storage.Ledger.GetAmount(Torch) == 1,
            "CPU advances to its next queued item after previous item completes");
    }

    private static void ContinueBoundedScan(RegressionWorld world, Action<bool, string> check)
    {
        var fixture = Setup(world);
        var third = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 43, 40);
        var fourth = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 44, 40);
        const int blockedJobsPerHost=40; // Fixture size exceeds the old cap without following a production limit.
        foreach (var cpu in new[] { fixture.First, fixture.Second, third })
            for (int job = 0; job < blockedJobsPerHost; job++) cpu.Enqueue(Stone, 1, "blocked-" + job);
        fourth.Enqueue(Torch, 1);
        Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.IsAir && fixture.Storage.Ledger.GetAmount(Wood) == 20,
            "large multi-CPU scan yields before exhausting all candidate planning in one tick");
        // A busy machine can yield after any candidate. Even one candidate per selection
        // must reach the runnable order; do not demand a machine-dependent second tick.
        for (int selection = 0; selection < blockedJobsPerHost * 3 + 1
            && fixture.Worker.ActiveKey.IsAir; selection++) Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.Equals(Torch) && ReferenceEquals(fourth.ActiveJob, fourth.Jobs[0]),
            "bounded scan resumes after prior candidates and reaches a later runnable CPU");
        Tick(fixture.Worker, 40);
        check(fourth.Jobs.Count == 0 && fixture.Storage.Ledger.GetAmount(Torch) == 1
            && fixture.First.Jobs.Count == blockedJobsPerHost && third.Jobs.Count == blockedJobsPerHost,
            "bounded scan does not discard blocked jobs while completing later feasible work");
    }

    private static void ContinueAfterWallClockYield(RegressionWorld world, Action<bool, string> check)
    {
        Recipe originalStone = Main.recipe[1];
        int conditionCalls = 0;
        try
        {
            // Deliberately exceed the cooperative budget inside a real recipe callback.
            // This tests continuation semantics, not an upper bound on machine latency.
            Main.recipe[1] = Recipe.Create(ItemID.StoneBlock).AddIngredient(ItemID.Gel).AddCondition(
                new Condition("AE2 regression slow candidate condition", () =>
                {
                    conditionCalls++;
                    long start = Stopwatch.GetTimestamp();
                    while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 20) { }
                    return false;
                }));
            StorageSystem.Recipes.Build();
            var fixture = Setup(world);
            fixture.First.Enqueue(Stone, 1, "slow-first");
            fixture.First.Enqueue(Stone, 1, "slow-second");
            fixture.Second.Enqueue(Torch, 1);
            string previousWorkerStatus = fixture.Worker.LastStatus;
            string previousCpuStatus = fixture.First.LastStatus;
            Tick(fixture.Worker, 10);
            check(conditionCalls > 0 && fixture.Worker.ActiveKey.IsAir && fixture.Second.ActiveJob == null
                && fixture.Storage.Ledger.GetAmount(Wood) == 20,
                "a slow real condition yields before planning the next candidate without consuming stock");
            check(fixture.Worker.LastStatus == previousWorkerStatus && fixture.First.LastStatus == previousCpuStatus,
                "yielding within a CPU does not diagnose its unexamined queue as missing materials");

            int additions = 0;
            for (int selection = 0; selection < 4 && fixture.Worker.ActiveKey.IsAir; selection++)
            {
                fixture.Storage.Ledger.Restore(Wood, 1); additions++;
                Tick(fixture.Worker, 10);
                if (fixture.Worker.ActiveKey.IsAir)
                    check(fixture.Worker.LastStatus == previousWorkerStatus,
                        "yielding between CPUs keeps the prior worker status rather than calling unexamined work missing");
            }
            check(fixture.Worker.ActiveKey.Equals(Torch) && ReferenceEquals(fixture.Second.ActiveJob, fixture.Second.Jobs[0])
                && fixture.First.Jobs.Count == 2,
                "inventory changes invalidate failure results without restarting a yielded expensive queue prefix");
            Tick(fixture.Worker, 40);
            check(fixture.Second.Jobs.Count == 0 && fixture.Storage.Ledger.GetAmount(Torch) == 1
                && fixture.Storage.Ledger.GetAmount(Wood) == 19 + additions && fixture.First.Jobs.Count == 2,
                "work reached after a timed yield commits once and preserves blocked orders");
        }
        finally
        {
            Main.recipe[1] = originalStone;
            StorageSystem.Recipes.Build();
        }
    }

    private static void RetryAfterConditionChange(RegressionWorld world, Action<bool, string> check)
    {
        Main.recipe[0] = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddCondition(Condition.TimeDay);
        StorageSystem.Recipes.Build(); Main.dayTime = true;
        var fixture = Setup(world);
        fixture.Second.Enqueue(Torch, 2);
        Tick(fixture.Worker, 49);
        check(fixture.Storage.Ledger.GetAmount(Torch) == 1 && fixture.Second.Jobs[0].Remaining == 1,
            "conditional CPU batch completes through real update before retry timing test");
        Main.dayTime = false; Tick(fixture.Worker, 10);
        check(fixture.Worker.ActiveKey.IsAir && fixture.Second.Jobs[0].Remaining == 1,
            "condition failure leaves CPU order queued without consuming next batch");
        Main.dayTime = true; Tick(fixture.Worker, 70);
        check(fixture.Worker.ActiveKey.Equals(Torch) && fixture.Second.ActiveJob != null,
            "failed selection expires after elapsed ticks even when polling is not aligned to tick multiples");
        Tick(fixture.Worker, 40);
        check(fixture.Second.Jobs.Count == 0 && fixture.Storage.Ledger.GetAmount(Torch) == 2,
            "CPU condition recovery completes preserved work without requiring unrelated inventory changes");
    }
}
