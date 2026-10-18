using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.Content.Items;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

/// <summary>Opt-in tests against the actually loaded mod pack. Never opens or saves a world/player.</summary>
public static class RegressionChecks
{
    private static int _passed;
    internal static bool IsRunning { get; private set; }
    private sealed class Storage : IMEStorage
    {
        public StorageLedger Ledger { get; } = new(8,8,int.MaxValue);
        public bool Allows(ItemKey key) => true;
    }
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("AE2 REGRESSION FAILED: " + name);
        _passed++;
        AE2ForTerraria.Instance.Logger.Info("AE2 REGRESSION PASS: " + name);
    }
    public static void RunIfRequested()
    {
        if (Environment.GetEnvironmentVariable("AE2_RUN_REGRESSION") != "1") return;
        _passed = 0;
        try
        {
            IsRunning = true;
            LedgerChecks(); InventoryChecks(); PersistenceChecks(); CraftChecks(); TopologyChecks(); WireChecks();
            var failures = new List<string>();
            void Suite(string name, Action<Action<bool,string>> run)
            {
                try { run(Check); }
                catch (Exception ex) { failures.Add(name); AE2ForTerraria.Instance.Logger.Error("AE2 SUITE FAILED: " + name, ex); }
            }
            Suite("machine tiles", MachineTileRegression.Run);
            Suite("integrated terminal crafting", TerminalCraftingRegression.Run);
            Suite("terminal entrance access", TerminalEntranceRegression.Run);
            Suite("inventory pages", InventoryPageRegression.Run);
            Suite("bridge recovery", BridgeRecoveryRegression.Run);
            Suite("digital singularity", DigitalSingularityRegression.Run);
            Suite("disk tier capacities", DiskCapacityRegression.Run);
            Suite("craftability", CraftabilityRegression.Run);
            Suite("progression routes", ProgressionRegression.Run);
            Suite("storage bus", StorageBusRegression.Run);
            Suite("storage upgrade", StorageUpgradeRegression.Run);
            Suite("autocrafting operations", AutocraftingRegression.Run);
            Suite("bus performance", BusPerformanceRegression.Run);
            Suite("planner performance", PlannerPerformanceRegression.Run);
            Suite("inventory deposit", InventoryDepositRegression.Run);
            Suite("cursor transaction acknowledgements", CursorTransactionRegression.Run);
            Suite("world-range drop collection", DropCollectorRegression.Run);
            Suite("player state packet", PlayerStateRegression.Run);
            Suite("planner alternatives", PlannerRegression.Run);
            Suite("planner quantity boundaries", PlannerQuantityRegression.Run);
            Suite("multiple crafting CPUs", MultiCpuRegression.Run);
            Suite("craft permissions and commit state", CraftPermissionRegression.Run);
            Suite("bus mutable custom data", BusScanRegression.Run);
            Suite("bus exception atomicity", BusTransactionRegression.Run);
            Suite("fragment state transitions", FragmentRegression.Run);
            if (failures.Count > 0) throw new InvalidOperationException("Failed suites: " + string.Join(", ", failures));
            AE2ForTerraria.Instance.Logger.Info("AE2 REGRESSION COMPLETE: " + _passed + " passed; no world/player loaded or saved.");
        }
        catch (Exception ex)
        { AE2ForTerraria.Instance.Logger.Error("AE2 REGRESSION FAILED", ex); }
        finally { IsRunning = false; }
    }
    private static void LedgerChecks()
    {
        var key = ItemKey.Crafted(ItemID.Wood);
        var ledger = new StorageLedger(8,8,63) { MaxBytes = 9 };
        var item = new Item(ItemID.Wood,20);
        Check(ledger.InsertItem(item) == 8 && item.stack == 12, "partial deposit conserves remainder");
        Check(ledger.UsedBytes == 9 && ledger.Insert(key,1) == 0, "exact capacity refuses overflow");
        ledger.Extract(key,1);
        Check(ledger.Insert(key,1) == 1, "fractional byte can be filled again");
        long before = ledger.StoredTotalAmount;
        ledger.Insert(key,50,true);
        Check(ledger.StoredTotalAmount == before, "simulation does not mutate");
        ledger.MaxBytes = 0;
        ledger.Restore(ItemKey.Crafted(ItemID.StoneBlock),15);
        Check(ledger.StoredTotalAmount == 23, "restore preserves over-capacity contents");
        Check(ledger.Insert(key,1) == 0 && ledger.Extract(key,1) == 1, "over-capacity storage remains withdrawable");
        var huge = new StorageLedger(); huge.Restore(key,long.MaxValue);
        Check(huge.Insert(key,1) == 0 && huge.StoredTotalAmount == long.MaxValue, "long quantity cannot wrap");
        var unit = new Storage(); unit.Ledger.MaxBytes = 9; unit.Ledger.Insert(key,8);
        var consume = new Dictionary<ItemKey,long> { [key]=1 };
        var output = new Dictionary<ItemKey,long> { [ItemKey.Crafted(ItemID.StoneBlock)]=1 };
        Check(!StorageTransaction.TryApply(new() {unit},consume,output,out _) && unit.Ledger.GetAmount(key)==8, "failed craft leaves all ingredients intact");
        unit.Ledger.MaxBytes=64;
        Check(StorageTransaction.TryApply(new() {unit},consume,output,out _) && unit.Ledger.GetAmount(key)==7
            && unit.Ledger.GetAmount(ItemKey.Crafted(ItemID.StoneBlock))==1, "successful craft commits both sides");
        NetworkInventory.Reset();
        var units = new List<IMEStorage> { unit };
        var snapshot = MENetwork.Aggregate(units);
        bool reused=true;
        for (int i=0;i<1000;i++) reused &= ReferenceEquals(snapshot,MENetwork.Aggregate(units));
        Check(reused,"unchanged aggregate reused across 1000 reads");
        Check(NetworkInventory.BuildCount==1,"one aggregate build for 1001 unchanged reads");
        unit.Ledger.Extract(key,1);
        Check(!ReferenceEquals(snapshot,MENetwork.Aggregate(units)) && NetworkInventory.BuildCount==2,"ledger mutation invalidates aggregate exactly once");
    }
    private static void InventoryChecks()
    {
        int oldMode=Main.netMode;
        var oldItems=Main.item;
        try
        {
            Main.netMode=NetmodeID.SinglePlayer;
            var player=new Player();
            player.inventory[0]=new Item(ItemID.Wood,20); player.inventory[1]=new Item(ItemID.Gel,30);
            player.inventory[2]=new Item(ItemID.Wood,40) {favorited=true};
            var storage=new Storage(); storage.Ledger.MaxBytes=9;
            long moved=StorageOperations.DepositInventory(new() {storage},player,onlySlot:0);
            Check(moved==8 && player.inventory[0].stack==12 && player.inventory[1].stack==30,
                "single-slot operation never deposits unrelated inventory and retains partial remainder");
            storage.Ledger.MaxBytes=100;
            moved=StorageOperations.DepositInventory(new() {storage},player,onlyType:ItemID.Wood);
            Check(moved==12 && player.inventory[2].stack==40 && player.inventory[1].stack==30,
                "same-type deposit protects favorites and other types");
            Main.item=new Item[Main.maxItems+1];
            for(int i=0;i<Main.item.Length;i++) Main.item[i]=new Item(ItemID.StoneBlock) {active=true};
            long before=storage.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood));
            moved=StorageOperations.Withdraw(new() {storage},ItemKey.Crafted(ItemID.Wood),50,player);
            Check(moved==0 && storage.Ledger.GetAmount(ItemKey.Crafted(ItemID.Wood))==before,
                "full world-drop slots never overwrite drops or debit storage");
        }
        finally {Main.netMode=oldMode; Main.item=oldItems;}
    }
    private static void PersistenceChecks()
    {
        var drive = new MEDiskDriveEntity(); drive.Disks[0] = new Item(ModContent.ItemType<MEDisk1K>()); drive.RefreshCapacity();
        var sword = new Item(ItemID.CopperShortsword); sword.Prefix(PrefixID.Legendary);
        ItemKey key = ItemKey.FromItem(sword); drive.Ledger.Insert(key,3);
        var tag = new TagCompound(); drive.SaveData(tag);
        var loaded = new MEDiskDriveEntity(); loaded.LoadData(tag);
        Check(loaded.Ledger.GetAmount(key)==3 && loaded.Ledger.MaxBytes>0, "drive save/load restores stock and disk capacity");
        var legacy = new TagCompound { ["entries"] = new List<TagCompound> { new() { ["type"]=ItemID.Wood,["prefix"]=0,["amount"]=21L } } };
        var diskless = new MEDiskDriveEntity(); diskless.LoadData(legacy);
        Check(diskless.Ledger.StoredTotalAmount==21 && diskless.Ledger.MaxBytes==0, "legacy diskless entries are retained");
        var entry = new TagCompound { ["type"]=ItemID.DirtBlock, ["amount"]=4L };
        ItemPersistence.WriteKey(entry,ItemKey.Crafted(ModContent.ItemType<MEDisk1K>()));
        Check(ItemPersistence.ReadKey(entry).Type==ModContent.ItemType<MEDisk1K>(), "stable ItemIO identity takes precedence over obsolete numeric id");
        var unknownTag = new TagCompound { ["mod"]="AE2RegressionMissingMod", ["name"]="OriginalItem", ["stack"]=1,
            ["data"]=new TagCompound { ["charge"]=123, ["owner"]="retained" } };
        var missingFilterTag=new TagCompound { ["filterItems"]=new List<TagCompound> {unknownTag} };
        var missingFilter=ItemPersistence.LoadFilter(missingFilterTag,"filter");
        var filteredDrive=new MEDiskDriveEntity { Filter=missingFilter,IsWhitelist=true };
        Check(!filteredDrive.Allows(ItemKey.Crafted(ItemID.Wood)),"missing mod whitelist never becomes accept-all");
        var filterSaved=new TagCompound(); ItemPersistence.SaveFilter(filterSaved,"filter",missingFilter);
        Check(filterSaved.GetList<TagCompound>("filterItems").Count==1,"missing filter identity survives saving while mod is disabled");
        Item unknown = ItemIO.Load(unknownTag);
        ItemKey unknownKey = ItemKey.FromItem(unknown);
        var saved = new TagCompound(); ItemPersistence.WriteKey(saved,unknownKey);
        ItemKey roundTrip = ItemPersistence.ReadKey(saved);
        Check(unknownKey.Equals(roundTrip) && unknownKey.Data!=null, "missing mod placeholder retains original custom data");
        Check(ItemKey.FromItem(key.CreateItem()).Equals(key), "prefix survives physical item reconstruction");
        var cpu = new MECraftingCpuEntity();
        Check(cpu.Enqueue(ItemKey.Crafted(ItemID.Wood),2,"A") && cpu.Enqueue(ItemKey.Crafted(ItemID.Wood),2,"B") && cpu.Jobs.Count==2,
            "different requesters do not merge delivery ownership");
        var numericCpu=new MECraftingCpuEntity();
        Check(numericCpu.Enqueue(ItemKey.Crafted(ItemID.Wood),long.MaxValue)
            && numericCpu.Jobs[0].Remaining==long.MaxValue,"order accepts full positive long quantity");
        Check(!numericCpu.Enqueue(ItemKey.Crafted(ItemID.Wood),0)
            && !numericCpu.Enqueue(ItemKey.Crafted(ItemID.Wood),-1)
            && !numericCpu.Enqueue(ItemKey.None,1),"order rejects nonpositive quantities and invalid identities");
        Check(numericCpu.Enqueue(ItemKey.Crafted(ItemID.Wood),1)
            && numericCpu.Jobs.Count==2 && numericCpu.Jobs[0].Remaining==long.MaxValue
            && numericCpu.Jobs[1].Remaining==1,"quantity exceeding one job's numeric range remains a separate exact order");
        string firstId=cpu.Jobs[0].Id,secondId=cpu.Jobs[1].Id;
        cpu.MoveJob(cpu.Jobs[1],-999); cpu.DropJob(cpu.FindJob(firstId));
        Check(cpu.Jobs.Count==1 && cpu.Jobs[0].Id==secondId,"stale queue order cannot redirect cancellation to another job");
        var cpuTag=new TagCompound(); cpu.SaveData(cpuTag); var loadedCpu=new MECraftingCpuEntity(); loadedCpu.LoadData(cpuTag);
        Check(loadedCpu.Jobs[0].Id==secondId,"queue identity survives save and load");
        var legacyCpu=new MECraftingCpuEntity(); cpuTag.GetList<TagCompound>("jobs")[0].Remove("jobId"); legacyCpu.LoadData(cpuTag);
        Check(!string.IsNullOrEmpty(legacyCpu.Jobs[0].Id),"legacy queue receives a stable identity");
    }
    private static void CraftChecks()
    {
        Recipe[] oldRecipes = Main.recipe; int oldCount = Recipe.numRecipes;
        try
        {
            Recipe a = Recipe.Create(ItemID.Torch,3).AddIngredient(ItemID.Wood,1).AddIngredient(ItemID.Gel,1);
            Recipe b = Recipe.Create(ItemID.Wood,4).AddIngredient(ItemID.StoneBlock,1);
            Recipe c = Recipe.Create(ItemID.DirtBlock,1).AddIngredient(ItemID.DirtBlock,1);
            Main.recipe = new[] { a,b,c }; Recipe.numRecipes=3;
            var index = new RecipeIndex(); index.Build(); var planner = new CraftPlanner(index);
            var ledger = new StorageLedger(1,1,int.MaxValue); ledger.Insert(ItemKey.Crafted(ItemID.StoneBlock),1); ledger.Insert(ItemKey.Crafted(ItemID.Gel),1);
            var plan = planner.Plan(ledger,ItemKey.Crafted(ItemID.Torch),1,useExistingTarget:false);
            Check(plan.FullySatisfied && plan.Outputs[ItemKey.Crafted(ItemID.Torch)]==3, "exact batch output follows chosen recipe");
            Check(plan.Outputs[ItemKey.Crafted(ItemID.Wood)]==3, "unused intermediate batch output retained");
            Check(plan.Consumed[ItemKey.Crafted(ItemID.StoneBlock)]==1 && plan.Consumed[ItemKey.Crafted(ItemID.Gel)]==1, "recursive leaf consumption matches outputs");
            ledger.Insert(ItemKey.Crafted(ItemID.Torch),5);
            plan=planner.Plan(ledger,ItemKey.Crafted(ItemID.Torch),1,useExistingTarget:false);
            Check(!plan.Consumed.ContainsKey(ItemKey.Crafted(ItemID.Torch)) && plan.ProducedAmount==1, "manufacture never consumes existing target stock");
            plan=planner.Plan(ledger,ItemKey.Crafted(ItemID.Torch),1,_=>false,false);
            Check(!plan.FullySatisfied, "unavailable station/condition rejects manufacture");
            plan=planner.Plan(ledger,ItemKey.Crafted(ItemID.DirtBlock),1);
            Check(!plan.FullySatisfied, "cyclic recipe terminates as missing");
            var special=Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Gel).AddOnCraftCallback((_,_,_,_)=>throw new InvalidOperationException("must not execute during planning"));
            Main.recipe=new[] {special}; Recipe.numRecipes=1; index.Build();
            plan=planner.Plan(ledger,ItemKey.Crafted(ItemID.Torch),1,useExistingTarget:false);
            Check(!plan.FullySatisfied && plan.BlockedReasons.Count==1,"special callbacks are never silently bypassed or executed during preview");
            Recipe grouped=Recipe.Create(ItemID.WoodenSword).AddRecipeGroup(RecipeGroupID.Wood,2);
            Main.recipe=new[] {grouped}; Recipe.numRecipes=1; index.Build();
            var mixed=new StorageLedger(1,1,int.MaxValue);
            mixed.Insert(ItemKey.Crafted(ItemID.BorealWood),1); mixed.Insert(ItemKey.Crafted(ItemID.RichMahogany),1);
            plan=planner.Plan(mixed,ItemKey.Crafted(ItemID.WoodenSword),1,useExistingTarget:false);
            Check(plan.FullySatisfied && plan.Consumed.Count==2,"recipe group accepts mixed alternative ingredients");
            Check(index.GetRecipesUsing(ItemID.BorealWood).Count==1,"usage index includes group alternatives");
            var assembler=new MEMolecularAssemblerEntity(); assembler.Stations[0]=new Item(ItemID.MythrilAnvil);
            Check(assembler.HasStation(TileID.Anvils),"advanced anvil satisfies basic anvil recipes");
            var conditional=Recipe.Create(ItemID.Wood).AddCondition(new Condition("AE2 regression false condition",()=>false));
            Check(!assembler.CanUseRecipe(conditional),"environment condition enforced by assembler");
            var cpu=new MECraftingCpuEntity(); cpu.Enqueue(ItemKey.Crafted(ItemID.Torch),1); cpu.Paused=true;
            var jobField=typeof(MEMolecularAssemblerEntity).GetField("_activeJob",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var cpuField=typeof(MEMolecularAssemblerEntity).GetField("_activeCpu",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            jobField.SetValue(assembler,cpu.Jobs[0]); cpuField.SetValue(assembler,cpu); assembler.ActiveKey=cpu.Jobs[0].Key;
            assembler.Update();
            Check(assembler.ActiveKey.IsAir && cpu.Jobs[0].Remaining==1,"pause stops an already active job before consuming materials");
            cpu.Paused=false; jobField.SetValue(assembler,cpu.Jobs[0]); cpuField.SetValue(assembler,cpu); assembler.ActiveKey=cpu.Jobs[0].Key; cpu.ClearQueue();
            assembler.Update();
            Check(assembler.ActiveKey.IsAir,"cancel clears assembler's outstanding job reference");
        }
        finally { Main.recipe=oldRecipes; Recipe.numRecipes=oldCount; }
    }
    private static void TopologyChecks()
    {
        var registry=ModContent.GetInstance<NetworkRegistry>(); registry.ClearWorld();
        var a=new Terraria.DataStructures.Point16(10,10); var b=new Terraria.DataStructures.Point16(11,10);
        NetworkRegistry.TryClaim(a,"A"); NetworkRegistry.SetName(a,"Original"); NetworkRegistry.SetLocked(a,ItemID.Wood,true);
        NetworkRegistry.TrackComponent(new() {a,b},a);
        NetworkRegistry.TrackComponent(new() {b},b);
        Check(NetworkRegistry.GetOwner(b)=="A" && NetworkRegistry.GetName(b)=="Original" && NetworkRegistry.IsLocked(b,ItemID.Wood),
            "anchor removal preserves name owner and locked items");
        var c=new Terraria.DataStructures.Point16(12,10); NetworkRegistry.TryClaim(c,"B"); NetworkRegistry.TrackComponent(new() {c},c);
        NetworkRegistry.TrackComponent(new() {b,c},b);
        Check(NetworkRegistry.GetLevel(b,"A")==NetworkRegistry.LevelNone && NetworkRegistry.GetLevel(b,"B")==NetworkRegistry.LevelNone,
            "merging different owners never escalates access");
        NetworkRegistry.TrackComponent(new() {b},b); NetworkRegistry.TrackComponent(new() {c},c);
        Check(NetworkRegistry.GetOwner(b)=="A" && NetworkRegistry.GetOwner(c)=="B", "split restores each original owner after a restricted merge");
        var oldWorld=new TagCompound { ["networkOwners"]=new List<TagCompound> { new() { ["k"]="10,10",["v"]="A" } } };
        registry.LoadWorldData(oldWorld);
        Check(NetworkRegistry.GetOwner(a)=="A","ownership loads even when old world has no names table");
        registry.ClearWorld(); NetworkInventory.Reset();
    }
    private static void WireChecks()
    {
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
        var write=typeof(MENet).GetMethod("WriteLedger",flags);
        var read=typeof(MENet).GetMethod("ReadLedgerInto",flags);
        var source=new StorageLedger(8,8,2) { MaxBytes=0 };
        var original=ItemIO.Load(new TagCompound { ["mod"]="AE2RegressionMissingMod",["name"]="WireItem",["stack"]=1,["data"]=new TagCompound { ["value"]=77 } });
        var key=ItemKey.FromItem(original); source.Restore(key,200);
        using var stream=new MemoryStream();
        using var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true);
        write.Invoke(null,new object[] {writer,source}); stream.Position=0;
        var target=new StorageLedger(); using var reader=new BinaryReader(stream);
        read.Invoke(null,new object[] {reader,target});
        Check(target.GetAmount(key)==200 && target.MaxBytes==0,"wire snapshot preserves custom data and over-capacity quantities");
        Check(stream.Position==stream.Length,"wire snapshot consumes exactly its payload");
        byte[] truncated=stream.ToArray()[..^1];
        var previous=new StorageLedger(); previous.Insert(ItemKey.Crafted(ItemID.Wood),9);
        bool rejected=false;
        try { using var broken=new BinaryReader(new MemoryStream(truncated)); read.Invoke(null,new object[] {broken,previous}); }
        catch(System.Reflection.TargetInvocationException ex) when(ex.InnerException is IOException) {rejected=true;}
        Check(rejected && previous.GetAmount(ItemKey.Crafted(ItemID.Wood))==9,"truncated snapshot preserves previous ledger view");
    }
}
