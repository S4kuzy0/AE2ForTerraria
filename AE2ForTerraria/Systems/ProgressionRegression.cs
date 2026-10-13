using System;
using System.Collections.Generic;
using System.Reflection;
using AE2ForTerraria.Core;
using AE2ForTerraria.Progression;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

internal static class ProgressionRegression
{
    internal static void Run(Action<bool,string> check)
    {
        if(Terraria.ModLoader.ModLoader.HasMod("BossChecklist"))
        {
            var stages=new List<ProgressionTier>();
            check(new BossChecklistProvider().TryBuild(stages) && stages.Count>10 && stages.Exists(t=>t.NpcIds.Contains(NPCID.MoonLordCore))
                && stages.Exists(t=>t.IsDowned!=null),"actual BossChecklist API supplies the standard vanilla and mod timeline with live defeat callbacks");
        }
        var recipes=Main.recipe; int count=Recipe.numRecipes;
        try
        {
            ProgressionGraph Graph(params Recipe[] values)
            {
                Main.recipe=values;Recipe.numRecipes=values.Length;
                var index=new RecipeIndex();index.Build();var graph=new ProgressionGraph(index);
                graph.Tiers.Add(new(0,"start","开局","fixture"));
                var eye=new ProgressionTier(1,"bc_fixture_eye","克苏鲁之眼","fixture");eye.NpcIds.Add(NPCID.EyeofCthulhu);graph.Tiers.Add(eye);
                var wall=new ProgressionTier(2,"bc_fixture_wall","血肉墙","fixture");wall.NpcIds.Add(NPCID.WallofFlesh);graph.Tiers.Add(wall);
                graph.AnchorItem(ItemKey.Crafted(ItemID.Wood),"start");
                graph.AnchorItem(ItemKey.Crafted(ItemID.StoneBlock),"start");
                graph.AnchorItem(ItemKey.Crafted(ItemID.Ruby),"bc_fixture_eye");
                return graph;
            }
            var graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Ruby).AddIngredient(ItemID.Wood));
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==1,"crafted progression inherits a material's explicit boss unlock");
            graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Ruby),Recipe.Create(ItemID.Torch).AddIngredient(ItemID.StoneBlock));
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==0,"multiple recipes choose the earliest obtainable progression route");
            graph=Graph(Recipe.Create(ItemID.Emerald).AddIngredient(ItemID.Sapphire),Recipe.Create(ItemID.Emerald).AddIngredient(ItemID.Wood),
                Recipe.Create(ItemID.Sapphire).AddIngredient(ItemID.Emerald),Recipe.Create(ItemID.Sapphire).AddIngredient(ItemID.Ruby));
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Emerald))==0 && graph.GetTierIndex(ItemKey.Crafted(ItemID.Sapphire))==0,
                "cyclic alternative recipes converge to the earliest source independently of query order");
            graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddCondition(Condition.Hardmode));
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==2,"permanent recipe environment gates participate in boss progression");
            graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddTile(TileID.MythrilAnvil));
            graph.AnchorItem(ItemKey.Crafted(ItemID.MythrilAnvil),"bc_fixture_wall");
            ((Dictionary<int,List<int>>)typeof(ProgressionGraph).GetField("_stationItems",BindingFlags.Instance|BindingFlags.NonPublic)!
                .GetValue(graph))[TileID.MythrilAnvil]=new(){ItemID.MythrilAnvil};
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==2,"a later workstation prevents an early-material recipe from sorting before its unlock");
            graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddCondition(new Condition("Mods.AE2ForTerraria.UnknownGate",()=>true)));
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))<0,"an opaque mod condition remains unknown instead of becoming a fabricated early unlock");
            var drops=(Dictionary<int,List<DropRecord>>)typeof(GameDataIndex).GetField("NpcDrops",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);
            bool had=drops.TryGetValue(NPCID.EyeofCthulhu,out var prior);
            try
            {
                drops[NPCID.EyeofCthulhu]=new(){new DropRecord(ItemID.Torch,NPCID.EyeofCthulhu,0,1,1,1,Array.Empty<Terraria.GameContent.ItemDropRules.IItemDropRuleCondition>())};
                graph=Graph();
                typeof(ProgressionGraph).GetMethod("AnchorItemsFromBosses",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(graph,null);
                check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==1,"BossChecklist bc identifiers anchor real drop records by NPC identity");
            }
            finally {if(had)drops[NPCID.EyeofCthulhu]=prior;else drops.Remove(NPCID.EyeofCthulhu);}
        }
        finally {Main.recipe=recipes;Recipe.numRecipes=count;}
    }
}
