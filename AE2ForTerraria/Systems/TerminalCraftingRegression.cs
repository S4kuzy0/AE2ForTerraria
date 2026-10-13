using System;
using System.Collections.Generic;
using AE2ForTerraria.Content.Tiles;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace AE2ForTerraria.Systems;

/// <summary>In-memory terminal operations; never loads or writes a player/world save.</summary>
public static class TerminalCraftingRegression
{
    private static readonly ItemKey Wood=ItemKey.Crafted(ItemID.Wood), Torch=ItemKey.Crafted(ItemID.Torch);
    public static void Run(Action<bool,string> check)
    {
        Recipe[] original=Main.recipe;int originalCount=Recipe.numRecipes;
        const int groupId=int.MaxValue-92;
        bool hadGroup=RecipeGroup.recipeGroups.TryGetValue(groupId,out var originalGroup);
        try
        {
            void Recipes(params Recipe[] recipes)
            {Main.recipe=recipes;Recipe.numRecipes=recipes.Length;StorageSystem.Recipes.Build();}
            Recipes(Recipe.Create(ItemID.Torch,3).AddIngredient(ItemID.Wood));
            using var world=new RegressionWorld();
            (MECraftingTerminalEntity Terminal,MEStorageUnitEntity Storage) Setup(long wood=20)
            {
                world.Reset();
                var terminal=world.Place(new MECraftingTerminalEntity(),ModContent.TileType<MECraftingTerminalTile>(),30,30);
                var storage=world.Place(new MEStorageUnitEntity(),ModContent.TileType<MEStorageUnitTile>(),31,30);
                storage.Ledger.MaxBytes=long.MaxValue;storage.Ledger.MaxTypes=long.MaxValue;
                storage.Ledger.Restore(Wood,wood);return (terminal,storage);
            }
            var f=Setup(100000);
            f.Storage.Ledger.Restore(Torch,7);
            check(f.Terminal.Enqueue(Torch,100002),"terminal accepts order above old 99999 cap");
            f.Terminal.Update();
            check(f.Terminal.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Torch)==100009 && f.Storage.Ledger.GetAmount(Wood)==66666,
                "terminal crafts full affordable batch immediately without CPU or assembler and preserves existing output");

            f=Setup();f.Terminal.Enqueue(Torch,3);var paused=f.Terminal.Jobs[0];paused.Paused=true;
            f.Terminal.Update();
            check(f.Storage.Ledger.GetAmount(Wood)==20 && paused.Remaining==3,"individual task pause does not consume materials");
            paused.Paused=false;f.Terminal.Update();
            check(f.Terminal.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Torch)==3,"individual task resumes with exact batch output");
            f.Terminal.Enqueue(Torch,3);f.Terminal.DropJob(f.Terminal.Jobs[0]);f.Terminal.Update();
            check(f.Storage.Ledger.GetAmount(Wood)==19 && f.Storage.Ledger.GetAmount(Torch)==3,"canceled terminal task never produces an extra batch");

            f=Setup();f.Storage.IsWhitelist=true;f.Storage.Filter.Add(ItemID.Wood);f.Storage.MarkFilterDirty();
            f.Terminal.Enqueue(Torch,3);f.Terminal.Update();
            check(f.Storage.Ledger.GetAmount(Wood)==20 && f.Terminal.Jobs[0].Remaining==3,"terminal rejected output preserves all ingredients and queue");
            f.Storage.Filter.Clear();f.Storage.MarkFilterDirty();f.Terminal.Update();
            check(f.Terminal.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Torch)==3,"terminal resumes after output space/filter becomes usable");

            f=Setup();var oldCpu=world.Place(new MECraftingCpuEntity(),ModContent.TileType<MECraftingCpuTile>(),32,30);
            var oldAssembler=world.Place(new MEMolecularAssemblerEntity(),ModContent.TileType<MEMolecularAssemblerTile>(),33,30);
            oldCpu.Enqueue(Torch,6);oldAssembler.Stations[0]=new Item(ItemID.WorkBench);
            Recipes(Recipe.Create(ItemID.Torch,3).AddIngredient(ItemID.Wood).AddTile(TileID.WorkBenches));
            f.Terminal.Update();oldAssembler.Update();
            check(oldCpu.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Torch)==6 && oldAssembler.Stations[0].type==ItemID.WorkBench,
                "terminal consumes old CPU queue once and reads old assembler station without moving its item");

            f=Setup();f.Terminal.Enqueue(Torch,3);f.Terminal.Update();
            check(f.Storage.Ledger.GetAmount(Wood)==20 && f.Terminal.Jobs[0].LastStatus.Contains("工作站"),"missing workstation waits with reason");
            f.Terminal.Stations[0]=new Item(ItemID.WorkBench);f.Terminal.EnsureStationSlots();
            check(f.Terminal.Stations.Length==2 && f.Terminal.Stations[1].IsAir,"terminal station insertion adds a free slot");
            f.Terminal.Update();check(f.Storage.Ledger.GetAmount(Torch)==3,"terminal installed station satisfies recipe requirement");

            bool environment=false;
            Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddCondition(new Condition("AE2 controlled environment",()=>environment)));
            f=Setup();f.Terminal.Enqueue(Torch,1);f.Terminal.Update();
            check(f.Storage.Ledger.GetAmount(Wood)==20 && f.Terminal.Jobs[0].LastStatus.Contains("环境"),"terminal preserves environment predicate and explains waiting");
            environment=true;f.Terminal.Update();check(f.Storage.Ledger.GetAmount(Torch)==1,"terminal retries after environment condition becomes true");

            f=Setup();var captured=f.Terminal;f.Terminal.Enqueue(Torch,1);
            Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddCondition(new Condition("AE2 controlled cancellation",()=>{captured.ClearQueue();return true;})));
            f.Terminal.Update();check(f.Storage.Ledger.GetAmount(Wood)==20 && f.Storage.Ledger.GetAmount(Torch)==0,"condition callback cancel is rechecked before terminal transaction");

            Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood));
            f=Setup();var anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(30,30));
            NetworkRegistry.TryClaim(anchor,"owner");f.Terminal.Enqueue(Torch,1,"requester");f.Terminal.Update();
            check(f.Storage.Ledger.GetAmount(Wood)==20 && f.Terminal.Jobs[0].LastStatus.Contains("权限"),"terminal rejects unauthorized named job");
            NetworkRegistry.SetLevel(anchor,"requester",NetworkRegistry.LevelUse);f.Terminal.Update();
            check(f.Terminal.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Torch)==1,"terminal retained job resumes after permission restoration");

            f=Setup();anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(30,30));
            NetworkRegistry.TryClaim(anchor,"originalOwner");
            var foreign=world.Place(new MEStorageUnitEntity(),ModContent.TileType<MEStorageUnitTile>(),34,30);
            var foreignAnchor=MENetwork.CanonicalAnchor(MENetwork.Collect(34,30));NetworkRegistry.TryClaim(foreignAnchor,"foreignOwner");
            f.Terminal.Enqueue(Torch,1); // Legacy/keep-stock jobs have no named requester.
            world.SetTile(32,30,ModContent.TileType<MECableTile>());world.SetTile(33,30,ModContent.TileType<MECableTile>());
            f.Terminal.Update();
            check(f.Terminal.Jobs.Count==1 && f.Storage.Ledger.GetAmount(Wood)==20 && f.Storage.Ledger.GetAmount(Torch)==0,
                "anonymous keep/legacy order cannot bypass another owner's merged-network policy");
            world.ClearTile(33,30);f.Terminal.Update();
            check(f.Terminal.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Torch)==1,
                "anonymous legacy order resumes under original host owner after foreign network disconnects");

            RecipeGroup.recipeGroups[groupId]=new RecipeGroup(()=>"AE2 terminal alternatives",ItemID.Wood,ItemID.StoneBlock);
            var grouped=Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood,2);grouped.acceptedGroups.Add(groupId);Recipes(grouped);
            f=Setup(1);f.Storage.Ledger.Restore(ItemKey.Crafted(ItemID.StoneBlock),1);f.Terminal.Enqueue(Torch,1);f.Terminal.Update();
            check(f.Storage.Ledger.GetAmount(Torch)==1 && f.Storage.Ledger.GetAmount(Wood)==0
                && f.Storage.Ledger.GetAmount(ItemKey.Crafted(ItemID.StoneBlock))==0,"terminal executor consumes mixed recipe group alternatives exactly");

            Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood));
            var inventory=new Dictionary<ItemKey,long>{{Wood,long.MaxValue},{ItemKey.Crafted(ItemID.StoneBlock),long.MaxValue}};
            var plan=StorageSystem.Planner.Plan(inventory,Torch,long.MaxValue,useExistingTarget:false);
            check(plan.FullySatisfied && plan.Consumed[Wood]==long.MaxValue && plan.Outputs[Torch]==long.MaxValue,
                "planner handles long boundary and network inventory exceeding single-ledger total without overflow");
            Recipes(Recipe.Create(ItemID.Torch,3).AddIngredient(ItemID.Wood));
            check(StorageSystem.Planner.Plan(inventory,Torch,long.MaxValue,useExistingTarget:false).Limited,
                "unrepresentable full output batch fails safely instead of wrapping");

            var persistent=new MECraftingTerminalEntity();persistent.Enqueue(Torch,long.MaxValue,"owner");
            persistent.Enqueue(Torch,1,"owner");persistent.Jobs[0].Paused=true;persistent.OutputToInventory=true;
            persistent.Stations[0]=new Item(ItemID.WorkBench);persistent.EnsureStationSlots();
            for(int i=0;i<40;i++) persistent.Enqueue(Torch,1,"requester"+i);
            for(int i=1;i<20;i++) persistent.SetKeep(ItemKey.Crafted(i),100000);
            var tag=new TagCompound();persistent.SaveData(tag);var restored=new MECraftingTerminalEntity();restored.LoadData(tag);
            check(restored.Jobs.Count==42 && restored.Jobs[0].Remaining==long.MaxValue && restored.Jobs[0].Paused
                && restored.Keep.Count==19 && restored.OutputToInventory && restored.Stations[0].type==ItemID.WorkBench,
                "terminal save restores unlimited lists, large amounts, per-task pause, destination and real station inventory");
            CapacityFallbackChecks(world,check);
            StationSlotChecks(world,check);
        }
        finally
        {
            Main.recipe=original;Recipe.numRecipes=originalCount;StorageSystem.Recipes.Build();
            if(hadGroup) RecipeGroup.recipeGroups[groupId]=originalGroup;else RecipeGroup.recipeGroups.Remove(groupId);
        }
    }

    private static void CapacityFallbackChecks(RegressionWorld world,Action<bool,string> check)
    {
        (MECraftingTerminalEntity Terminal,MEStorageUnitEntity Storage) Setup(Condition condition=null)
        {
            var recipe=Recipe.Create(ItemID.Torch,3).AddIngredient(ItemID.Wood);
            if(condition!=null) recipe.AddCondition(condition);
            Main.recipe=new[]{recipe};Recipe.numRecipes=1;StorageSystem.Recipes.Build();
            world.Reset();
            var terminal=world.Place(new MECraftingTerminalEntity(),ModContent.TileType<MECraftingTerminalTile>(),30,30);
            var storage=world.Place(new MEStorageUnitEntity(),ModContent.TileType<MEStorageUnitTile>(),31,30);
            storage.Ledger.MaxBytes=long.MaxValue;
            storage.Ledger.Restore(Wood,20);
            // Restored stock may exceed today's per-identity limit; new Torch has exactly one batch of room.
            storage.Ledger.MaxAmountPerType=3;
            terminal.Enqueue(Torch,6);
            return(terminal,storage);
        }
        var f=Setup();f.Terminal.Update();
        check(f.Storage.Ledger.GetAmount(Wood)==19 && f.Storage.Ledger.GetAmount(Torch)==3
            && f.Terminal.Jobs.Count==1 && f.Terminal.Jobs[0].Remaining==3,
            "output capacity permits one atomic batch even when the complete affordable order cannot fit");
        int version=f.Storage.Ledger.Version;f.Terminal.Update();
        check(f.Storage.Ledger.Version==version && f.Storage.Ledger.GetAmount(Wood)==19
            && f.Terminal.Jobs[0].Remaining==3 && f.Terminal.Jobs[0].LastStatus.Contains("空间"),
            "full single-batch output leaves materials, ledger revision and remaining order unchanged with a space reason");
        f.Storage.Ledger.Extract(Torch,3);f.Terminal.Update();
        check(f.Storage.Ledger.GetAmount(Wood)==18 && f.Storage.Ledger.GetAmount(Torch)==3 && f.Terminal.Jobs.Count==0,
            "partially completed capacity-limited order resumes exactly after output is withdrawn");

        f=Setup();f.Storage.Ledger.Restore(Torch,2);version=f.Storage.Ledger.Version;f.Terminal.Update();
        check(f.Storage.Ledger.Version==version && f.Storage.Ledger.GetAmount(Torch)==2
            && f.Storage.Ledger.GetAmount(Wood)==20 && f.Terminal.Jobs.Count==1 && f.Terminal.Jobs[0].Remaining==6,
            "existing output near the per-identity cap cannot be counted as production or consume materials for an oversized batch");

        f=Setup();f.Terminal.ClearQueue();f.Terminal.Enqueue(Torch,4);f.Terminal.Update();
        bool firstBatch=f.Terminal.Jobs.Count==1 && f.Terminal.Jobs[0].Remaining==1
            && f.Storage.Ledger.GetAmount(Wood)==19 && f.Storage.Ledger.GetAmount(Torch)==3;
        long withdrawn=f.Storage.Ledger.Extract(Torch,3);f.Terminal.Update();
        check(firstBatch && withdrawn==3 && f.Terminal.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Wood)==18
            && f.Storage.Ledger.GetAmount(Torch)==3 && withdrawn+f.Storage.Ledger.GetAmount(Torch)==6,
            "capacity-limited request for four completes in two triple-output batches and preserves both surplus items");

        int calls=0;MECraftingTerminalEntity captured=null;
        f=Setup(new Condition("AE2 capacity retry cancellation",()=>{if(++calls==3)captured.ClearQueue();return true;}));
        captured=f.Terminal;f.Terminal.Update();
        check(calls==3 && f.Terminal.Jobs.Count==0 && f.Storage.Ledger.GetAmount(Wood)==20 && f.Storage.Ledger.GetAmount(Torch)==0,
            "cancel during capacity fallback planning prevents both the rejected full order and the fallback batch");

        calls=0;Terraria.DataStructures.Point16 anchor=default;
        f=Setup(new Condition("AE2 capacity retry permission",()=>
        {if(++calls==3)NetworkRegistry.SetLevel(anchor,"capacityGuest",NetworkRegistry.LevelReadOnly);return true;}));
        anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(30,30));NetworkRegistry.TryClaim(anchor,"capacityOwner");
        NetworkRegistry.SetLevel(anchor,"capacityGuest",NetworkRegistry.LevelUse);
        f.Terminal.Jobs[0].RequestedBy="capacityGuest";f.Terminal.Update();
        check(calls>=3 && f.Terminal.Jobs[0].Remaining==6 && f.Storage.Ledger.GetAmount(Wood)==20
            && f.Storage.Ledger.GetAmount(Torch)==0 && f.Terminal.Jobs[0].LastStatus.Contains("权限"),
            "capacity retry revalidates permission revoked by a condition before committing a smaller batch");

        calls=0;
        f=Setup(new Condition("AE2 capacity retry exception",()=>
        {if(++calls>=3)throw new InvalidOperationException("AE2 controlled capacity fallback condition failure");return true;}));
        f.Terminal.Update();
        check(calls>=3 && f.Terminal.Jobs[0].Remaining==6 && f.Storage.Ledger.GetAmount(Wood)==20 && f.Storage.Ledger.GetAmount(Torch)==0,
            "condition failure during capacity fallback preserves materials and queue without committing either plan");
    }

    private static void StationSlotChecks(RegressionWorld world,Action<bool,string> check)
    {
        Player[] players=Main.player;int myPlayer=Main.myPlayer;Item mouse=Main.mouseItem;
        var activePosition=UISystem.ActiveTerminalPos;
        var positionProperty=typeof(UISystem).GetProperty(nameof(UISystem.ActiveTerminalPos));
        try
        {
            Main.player=new Player[players.Length];
            for(int i=0;i<Main.player.Length;i++) Main.player[i]=new Player();
            Main.myPlayer=0;Main.player[0].active=true;Main.player[0].name="AE2StationSlotTest";
            Main.player[0].Center=new Vector2(30*16+8,30*16+8);
            world.Reset();
            var terminal=world.Place(new MECraftingTerminalEntity(),ModContent.TileType<MECraftingTerminalTile>(),30,30);
            positionProperty.SetValue(null,terminal.Position);
            var slot=UIIntegratedCraftingPanel.CreateStationSlot(terminal.Position,0);
            var firstArray=terminal.Stations;
            Main.mouseItem=new Item(ItemID.WorkBench);
            slot.InteractionHandler(false);
            check(!ReferenceEquals(firstArray,terminal.Stations) && terminal.Stations.Length==2 && Main.mouseItem.IsAir,
                "first station interaction on unclaimed network claims owner and grows live station array");
            // Deliberately reuse the same actual UI control without Update/rebuild.
            slot.InteractionHandler(true);
            check(terminal.Stations[0].IsAir && slot.Getter().IsAir && Main.mouseItem.type==ItemID.WorkBench && Main.mouseItem.stack==1,
                "same-control left then right interaction across array growth conserves one workstation");
            slot.InteractionHandler(false);slot.InteractionHandler(false);
            check(terminal.Stations[0].IsAir && Main.mouseItem.type==ItemID.WorkBench && Main.mouseItem.stack==1,
                "same-control consecutive exchanges use current array and preserve cursor item");
            terminal.Stations=new[]{new Item(ItemID.Furnace),new Item()};
            check(slot.Getter().type==ItemID.Furnace,"station getter follows replacement snapshot array");
            var invalid=UIIntegratedCraftingPanel.CreateStationSlot(terminal.Position,999);
            invalid.InteractionHandler(false);
            check(terminal.Stations[0].type==ItemID.Furnace && Main.mouseItem.type==ItemID.WorkBench,
                "stale station index leaves live stations and held item intact");

            world.SetTile(31,30,ModContent.TileType<MECableTile>());
            var legacy=world.Place(new MEMolecularAssemblerEntity(),ModContent.TileType<MEMolecularAssemblerTile>(),32,30);
            legacy.Stations[0]=new Item(ItemID.IronAnvil);var disconnected=UIIntegratedCraftingPanel.CreateStationSlot(legacy.Position,0);
            check(disconnected.Getter().type==ItemID.IronAnvil,"connected legacy station resolves through terminal network");
            world.ClearTile(31,30);Main.mouseItem=new Item();disconnected.InteractionHandler(true);
            check(legacy.Stations[0].type==ItemID.IronAnvil && Main.mouseItem.IsAir && disconnected.Getter().IsAir,
                "station control cannot mutate disconnected host after topology changes");

            ModContent.GetInstance<NetworkRegistry>().ClearWorld();
            var anchor=MENetwork.CanonicalAnchor(MENetwork.Collect(30,30));NetworkRegistry.TryClaim(anchor,"OtherOwner");
            NetworkRegistry.SetLevel(anchor,Main.player[0].name,NetworkRegistry.LevelReadOnly);
            slot.InteractionHandler(true);
            check(terminal.Stations[0].type==ItemID.Furnace && Main.mouseItem.IsAir,
                "station control rechecks management permission at interaction time");
        }
        finally
        {
            Main.player=players;Main.myPlayer=myPlayer;Main.mouseItem=mouse;positionProperty.SetValue(null,activePosition);UIStatus.Clear();
        }
    }
}
