using System;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

// Real assembler Update and public queue / topology APIs. No private active-job injection.
public static class CraftPermissionRegression
{
    private const string Owner = "AE2RegressionOwner";
    private const string Requester = "AE2RegressionRequester";
    private const string OtherOwner = "AE2RegressionOtherOwner";
    private static readonly ItemKey Wood = ItemKey.Crafted(ItemID.Wood);
    private static readonly ItemKey Torch = ItemKey.Crafted(ItemID.Torch);

    private static (MEMolecularAssemblerEntity Worker, MEStorageUnitEntity Storage, MECraftingCpuEntity Cpu,
        Point16 Anchor) Setup(RegressionWorld world)
    {
        world.Reset();
        var worker = world.Place(new MEMolecularAssemblerEntity(), ModContent.TileType<MEMolecularAssemblerTile>(), 40, 40);
        var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 40, 41);
        var cpu = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 41, 40);
        storage.Ledger.Restore(Wood, 20);
        Point16 anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(40, 40));
        NetworkRegistry.TryClaim(anchor, Owner);
        NetworkRegistry.SetLevel(anchor, Requester, NetworkRegistry.LevelUse);
        return (worker, storage, cpu, anchor);
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
        Player[] originalPlayers = Main.player;
        try
        {
            MEConfig.Instance.CraftTicksPerItem = 40;
            Main.recipe = new[] { Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood) };
            Recipe.numRecipes = Main.recipe.Length;
            StorageSystem.Recipes.Build();
            Main.player = new Player[originalPlayers.Length];
            for (int index = 0; index < Main.player.Length; index++) Main.player[index] = new Player { active = false };
            using var world = new RegressionWorld();
            RevokeAndRestore(world, check);
            SkipDeniedCpu(world, check);
            RecheckAtCommit(world, check);
            RecheckCallbackState(world, check);
            MergeDifferentOwners(world, check);
            OfflineAndAnonymousCompatibility(world, check);
            LimitedPlanDiagnostics(world, check);
        }
        finally
        {
            Main.player = originalPlayers;
            Main.recipe = originalRecipes; Recipe.numRecipes = originalCount;
            StorageSystem.Recipes.Build(); MEConfig.Instance.CraftTicksPerItem = originalSpeed;
        }
    }

    private static void RevokeAndRestore(RegressionWorld world, Action<bool, string> check)
    {
        var f = Setup(world);
        f.Cpu.Enqueue(Torch, 1, Requester); f.Cpu.OutputToInventory = true;
        var job = f.Cpu.Jobs[0];
        Main.player[0].name = Requester; Main.player[0].active = true;
        Tick(f.Worker, 10);
        check(ReferenceEquals(f.Cpu.ActiveJob, job), "authorized named order starts through real assembler update");
        NetworkRegistry.SetLevel(f.Anchor, Requester, NetworkRegistry.LevelReadOnly);
        Tick(f.Worker, 50);
        check(job.Remaining == 1 && f.Cpu.Jobs.Contains(job) && f.Cpu.ActiveJob == null
            && f.Storage.Ledger.GetAmount(Wood) == 20 && f.Storage.Ledger.GetAmount(Torch) == 0
            && InventoryCount(Main.player[0], ItemID.Torch) == 0,
            "revoked named order preserves queue and cannot consume or deliver ordinary unlocked materials");
        // Switching output destination is not a substitute for retaining requester permission.
        f.Cpu.OutputToInventory = false;
        NetworkRegistry.SetLevel(f.Anchor, Requester, NetworkRegistry.LevelUse);
        Tick(f.Worker, 10);
        check(ReferenceEquals(f.Cpu.ActiveJob, job), "restored permission invalidates failed-selection cache immediately");
        Tick(f.Worker, 40);
        check(f.Cpu.Jobs.Count == 0 && f.Storage.Ledger.GetAmount(Torch) == 1
            && f.Storage.Ledger.GetAmount(Wood) == 19, "restored order settles its preserved quantity exactly once");
        Main.player[0].active = false;
    }

    private static void SkipDeniedCpu(RegressionWorld world, Action<bool, string> check)
    {
        var f = Setup(world);
        var second = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 42, 40);
        NetworkRegistry.SetLevel(f.Anchor, Requester, NetworkRegistry.LevelNone);
        f.Cpu.Enqueue(Torch, 1, Requester); second.Enqueue(Torch, 1, Owner);
        var denied = f.Cpu.Jobs[0]; var allowed = second.Jobs[0];
        Tick(f.Worker, 10);
        check(f.Cpu.ActiveJob == null && ReferenceEquals(second.ActiveJob, allowed),
            "unauthorized first CPU does not block an authorized later CPU");
        Tick(f.Worker, 40);
        check(denied.Remaining == 1 && f.Cpu.Jobs.Contains(denied) && second.Jobs.Count == 0
            && f.Storage.Ledger.GetAmount(Torch) == 1 && f.Storage.Ledger.GetAmount(Wood) == 19,
            "skipping denied orders leaves their quantities intact while authorized CPU completes");
    }

    private static void RecheckAtCommit(RegressionWorld world, Action<bool, string> check)
    {
        var f = Setup(world);
        bool revokeDuringPlanning = false;
        Main.recipe[0] = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddCondition(
            new Condition("AE2 regression revoke during commit planning", () =>
            {
                if (revokeDuringPlanning) NetworkRegistry.SetLevel(f.Anchor, Requester, NetworkRegistry.LevelNone);
                return true;
            }));
        StorageSystem.Recipes.Build();
        f.Cpu.Enqueue(Torch, 1, Requester);
        Tick(f.Worker, 10);
        check(f.Cpu.ActiveJob != null, "commit permission regression naturally selects authorized order");
        revokeDuringPlanning = true;
        Tick(f.Worker, 40);
        check(NetworkRegistry.GetLevel(f.Anchor, Requester) == NetworkRegistry.LevelNone
            && f.Cpu.Jobs.Count == 1 && f.Cpu.Jobs[0].Remaining == 1
            && f.Storage.Ledger.GetAmount(Wood) == 20 && f.Storage.Ledger.GetAmount(Torch) == 0,
            "permission is rechecked after recipe evaluation and immediately before transaction commit");
        Main.recipe[0] = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood);
        StorageSystem.Recipes.Build();
    }

    private static void MergeDifferentOwners(RegressionWorld world, Action<bool, string> check)
    {
        var f = Setup(world);
        var otherCpu = world.Place(new MECraftingCpuEntity(), ModContent.TileType<MECraftingCpuTile>(), 45, 40);
        var otherStorage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 46, 40);
        otherStorage.Ledger.Restore(Wood, 20);
        Point16 otherAnchor = MENetwork.CanonicalAnchor(MENetwork.Collect(45, 40));
        NetworkRegistry.TryClaim(otherAnchor, OtherOwner);
        NetworkRegistry.SetLevel(f.Anchor, OtherOwner, NetworkRegistry.LevelUse);
        f.Cpu.Enqueue(Torch, 1, Requester);
        var pending = f.Cpu.Jobs[0];
        Tick(f.Worker, 10);
        check(f.Cpu.ActiveJob != null && NetworkRegistry.GetLevel(otherAnchor, Requester) == NetworkRegistry.LevelNone,
            "requester can use original network but not separately owned adjacent network");
        for (int x = 42; x <= 44; x++) world.SetTile(x, 40, ModContent.TileType<MECableTile>());
        Point16 merged = MENetwork.CanonicalAnchor(MENetwork.Collect(40, 40));
        check(NetworkRegistry.GetLevel(merged, Requester) == NetworkRegistry.LevelNone
            && NetworkRegistry.GetLevel(merged, OtherOwner) >= NetworkRegistry.LevelUse,
            "physical merge computes the intersection of owner policies through actual permission API");
        Tick(f.Worker, 50);
        check(pending.Remaining == 1 && f.Storage.Ledger.GetAmount(Wood) == 20
            && otherStorage.Ledger.GetAmount(Wood) == 20
            && f.Storage.Ledger.GetAmount(Torch) + otherStorage.Ledger.GetAmount(Torch) == 0,
            "pending order cannot consume from a newly merged network without all required permissions");
        otherCpu.Enqueue(Torch, 1, OtherOwner);
        Tick(f.Worker, 50);
        check(pending.Remaining == 1 && otherCpu.Jobs.Count == 0
            && f.Storage.Ledger.GetAmount(Wood) + otherStorage.Ledger.GetAmount(Wood) == 39
            && f.Storage.Ledger.GetAmount(Torch) + otherStorage.Ledger.GetAmount(Torch) == 1,
            "merged network skips unauthorized order and runs independently authorized CPU work");
        world.ClearTile(43, 40);
        Point16 split = MENetwork.CanonicalAnchor(MENetwork.Collect(40, 40));
        check(NetworkRegistry.GetLevel(split, Requester) >= NetworkRegistry.LevelUse,
            "physical split restores original requester permission rather than retaining merge display state");
        Tick(f.Worker, 60);
        check(f.Cpu.Jobs.Count == 0 && f.Storage.Ledger.GetAmount(Torch) + otherStorage.Ledger.GetAmount(Torch) == 2,
            "disconnecting foreign network allows the same preserved order to finish");
    }

    private static void RecheckCallbackState(RegressionWorld world, Action<bool, string> check)
    {
        foreach (string scenario in new[] { "cancel", "pause", "disconnect CPU", "disconnect storage", "replace storage", "anonymous keep pause", "anonymous target change" })
        {
            var f = Setup(world);
            bool changeDuringPlanning = false;
            MEStorageUnitEntity replacement = null;
            Main.recipe[0] = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddCondition(
                new Condition("AE2 regression commit state: " + scenario, () =>
                {
                    if (!changeDuringPlanning) return true;
                    changeDuringPlanning = false;
                    switch (scenario)
                    {
                        case "cancel": f.Cpu.ClearQueue(); break;
                        case "pause": case "anonymous keep pause": f.Cpu.Paused = true; break;
                        case "disconnect CPU": world.ClearTile(f.Cpu.Position.X,f.Cpu.Position.Y); break;
                        case "disconnect storage": world.ClearTile(f.Storage.Position.X,f.Storage.Position.Y); break;
                        case "replace storage":
                            TileEntity.ByPosition.Remove(f.Storage.Position); TileEntity.ByID.Remove(f.Storage.ID);
                            replacement = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 40, 41);
                            replacement.Ledger.Restore(Wood,20);
                            break;
                        case "anonymous target change": f.Worker.Target=ItemKey.Crafted(ItemID.StoneBlock); break;
                    }
                    return true;
                }));
            StorageSystem.Recipes.Build();
            if (scenario == "anonymous keep pause")
            {
                f.Cpu.SetKeep(Torch,1);
                for(int tick=0;tick<30;tick++) f.Cpu.Update();
            }
            else if (scenario == "anonymous target change") f.Worker.Target=Torch;
            else f.Cpu.Enqueue(Torch,1,Requester);
            var job=f.Cpu.Jobs.Count==0 ? null : f.Cpu.Jobs[0];
            Tick(f.Worker,10);
            check(f.Worker.ActiveKey.Equals(Torch) && (job==null || ReferenceEquals(f.Cpu.ActiveJob,job)),
                "commit callback scenario naturally starts through Update: " + scenario);
            changeDuringPlanning=true;
            Tick(f.Worker,39); // The first real completion boundary, before any subsequent selection.
            check(!changeDuringPlanning && f.Worker.ActiveKey.IsAir && f.Worker.ProgressTicks==0
                && f.Cpu.ActiveJob==null && f.Storage.Ledger.GetAmount(Wood)==20
                && f.Storage.Ledger.GetAmount(Torch)==0 && (job==null || job.Remaining==1)
                && (replacement==null || (replacement.Ledger.GetAmount(Wood)==20 && replacement.Ledger.GetAmount(Torch)==0)),
                "callback mutation rejects transaction without stale source debit or active marker: " + scenario);
            if (scenario=="cancel")
            {
                check(f.Cpu.Jobs.Count==0,"callback cancellation stays canceled rather than re-enqueueing old job");
                f.Cpu.Enqueue(Torch,1,Requester);
            }
            else if (scenario=="anonymous target change")
            {
                check(f.Worker.Target.Equals(ItemKey.Crafted(ItemID.StoneBlock)),"callback target change preserves the new anonymous target");
                f.Worker.Target=Torch;
            }
            else if (scenario=="pause" || scenario=="anonymous keep pause") f.Cpu.Paused=false;
            else if (scenario=="disconnect CPU") world.SetTile(f.Cpu.Position.X,f.Cpu.Position.Y,ModContent.TileType<MECraftingCpuTile>());
            else if (scenario=="disconnect storage") world.SetTile(f.Storage.Position.X,f.Storage.Position.Y,ModContent.TileType<MEStorageUnitTile>());
            Tick(f.Worker,50);
            var current=replacement ?? f.Storage;
            check(current.Ledger.GetAmount(Wood)==19 && current.Ledger.GetAmount(Torch)==1
                && f.Cpu.Jobs.Count==0 && f.Cpu.ActiveJob==null
                && (replacement==null || f.Storage.Ledger.GetAmount(Wood)==20),
                "restored callback state completes once using current storage and current order: " + scenario);
        }
        Main.recipe[0]=Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood);
        StorageSystem.Recipes.Build();
    }

    private static void LimitedPlanDiagnostics(RegressionWorld world, Action<bool,string> check)
    {
        Recipe[] simple=Main.recipe;
        int simpleCount=Recipe.numRecipes;
        try
        {
            var recipes=new System.Collections.Generic.List<Recipe>();
            for(int index=0;index<18;index++)
            {
                recipes.Add(Recipe.Create(100+index).AddIngredient(101+index));
                recipes.Add(Recipe.Create(100+index).AddIngredient(101+index));
            }
            Main.recipe=recipes.ToArray(); Recipe.numRecipes=Main.recipe.Length;
            StorageSystem.Recipes.Build();
            var key=ItemKey.Crafted(100);
            var diagnostic=StorageSystem.Planner.Plan(new StorageLedger(),key,1,useExistingTarget:false);
            check(diagnostic.Limited && diagnostic.Missing.Count>0,"limited diagnostic fixture has both incomplete search and provisional missing items");
            var f=Setup(world); f.Worker.Target=key;
            Tick(f.Worker,10);
            check(f.Worker.ActiveKey.IsAir && f.Worker.LastStatus=="规划达到复杂度上限，请拆分任务",
                "direct target reports search limit before provisional missing ingredients");
            f=Setup(world);
            f.Cpu.Enqueue(ItemKey.Crafted(ItemID.StoneBlock),1,Requester); // A definite missing candidate precedes the limited candidate.
            f.Cpu.Enqueue(key,1,Requester);
            Tick(f.Worker,10);
            // Cooperative selection may yield after the first candidate on a busy machine.
            if (f.Cpu.LastStatus!="规划达到复杂度上限，请拆分任务") Tick(f.Worker,10);
            check(f.Worker.ActiveKey.IsAir && f.Cpu.LastStatus=="规划达到复杂度上限，请拆分任务"
                && f.Cpu.Jobs.Count==2 && f.Storage.Ledger.GetAmount(Wood)==20,
                "CPU reports limited candidate before claiming its whole queue lacks ingredients");
        }
        finally
        {
            Main.recipe=simple; Recipe.numRecipes=simpleCount; StorageSystem.Recipes.Build();
        }
    }

    private static void OfflineAndAnonymousCompatibility(RegressionWorld world, Action<bool, string> check)
    {
        var f = Setup(world);
        Main.player[0].name = Requester; Main.player[0].active = false;
        f.Cpu.OutputToInventory = true; f.Cpu.Enqueue(Torch, 1, Requester);
        Tick(f.Worker, 50);
        check(f.Cpu.Jobs.Count == 0 && f.Storage.Ledger.GetAmount(Torch) == 1
            && f.Storage.Ledger.GetAmount(Wood) == 19 && InventoryCount(Main.player[0], ItemID.Torch) == 0,
            "offline requester retains granted permission and output stays in network instead of disappearing");

        f = Setup(world);
        NetworkRegistry.SetLevel(f.Anchor, Requester, NetworkRegistry.LevelNone);
        f.Cpu.SetKeep(Torch, 1);
        for (int tick = 0; tick < 30; tick++) f.Cpu.Update();
        check(f.Cpu.Jobs.Count == 1 && string.IsNullOrEmpty(f.Cpu.Jobs[0].RequestedBy),
            "keep-stock enters a real anonymous queue without inventing an owner identity");
        Tick(f.Worker, 50);
        check(f.Cpu.Jobs.Count == 0 && f.Storage.Ledger.GetAmount(Torch) == 1,
            "anonymous keep-stock compatibility is retained for unlocked ordinary materials");

        f = Setup(world);
        f.Worker.Target = Torch;
        Tick(f.Worker, 50);
        check(f.Worker.Target.IsAir && f.Storage.Ledger.GetAmount(Torch) == 1,
            "legacy anonymous direct target retains ordinary manufacturing behavior");
    }

    private static long InventoryCount(Player player, int type)
    {
        long count = 0;
        foreach (Item item in player.inventory) if (item != null && item.type == type) count += item.stack;
        return count;
    }
}
