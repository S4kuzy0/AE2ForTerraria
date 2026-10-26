using System;
using System.Collections.Generic;
using System.Linq;
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
        CheckOldOnesArmyGates(check);
        if(Terraria.ModLoader.ModLoader.HasMod("BossChecklist"))
        {
            var stages=new List<ProgressionTier>();
            check(new BossChecklistProvider().TryBuild(stages) && stages.Count>10 && stages.Exists(t=>t.NpcIds.Contains(NPCID.MoonLordCore))
                && stages.Exists(t=>t.IsDowned!=null),"actual BossChecklist API supplies the standard vanilla and mod timeline with live defeat callbacks");
        }
        GameDataIndex.EnsureBuilt();
        AcquisitionAudit.Export();
        var live = StorageSystem.Progression;
        if(live != null)
        {
            var sourceAnchors = new Dictionary<ItemKey,int>(); ProgressionSources.Anchor(live,sourceAnchors);
            AE2ForTerraria.Instance.Logger.Info("AE2 PROGRESSION SOURCES: built=" + GameDataIndex.Built
                + "; dungeonSpirit=" + string.Join(";",GameDataIndex.GetNpcDropRecords(NPCID.DungeonSpirit).Select(d=>d.ItemType+":"+string.Join(",",d.Conditions.Select(c=>c.GetType().FullName))))
                + "; ectoplasm=" + sourceAnchors.GetValueOrDefault(ItemKey.Crafted(ItemID.Ectoplasm),-1)
                + "; plantera=" + ProgressionSources.BossTier(live,NPCID.Plantera));
            AE2ForTerraria.Instance.Logger.Info("AE2 EARLIEST SOURCE: LifeCrystal=" + live.GetTierIndex(ItemKey.Crafted(ItemID.LifeCrystal))
                + "; source=" + live.GetExplanation(ItemID.LifeCrystal));
            AE2ForTerraria.Instance.Logger.Info("AE2 EARLIEST FISHING: FrogLeg=" + live.GetTierIndex(ItemKey.Crafted(ItemID.FrogLeg))
                + "; source=" + live.GetExplanation(ItemID.FrogLeg));
            check(live.GetTierIndex(ItemKey.Crafted(ItemID.FrogLeg))==0 && live.GetExplanation(ItemID.FrogLeg).Contains("钓鱼"),
                "frog leg uses its direct pre-boss fishing source instead of a later Blood Moon drop or crafting route");
            check(live.Tiers.Count(t=>t.NpcIds.Contains(NPCID.TorchGod)) == 1,
                "Torch God has one canonical checkpoint rather than a translated duplicate inferred stage");
            foreach (int item in new[] { ItemID.Squirrel, ItemID.Bunny, ItemID.Bird, ItemID.Frog, ItemID.Worm, ItemID.GoldWorm, ItemID.FairyCritterPink, ItemID.TreeNymphButterfly, ItemID.PurpleEmperorButterfly })
                check(live.GetTierIndex(ItemKey.Crafted(item)) == 0, "capturable native critter has a pre-boss Bug Net route: " + item);
            check(live.GetTierIndex(ItemKey.Crafted(ItemID.TruffleWorm)) == ProgressionSources.BossTier(live,NPCID.WallofFlesh),
                "Truffle Worm capture remains hardmode instead of treating every critter as pre-boss");
            check(ProgressionWorldSources.Routes(live).Where(r=>r.Output==ItemID.EmpressButterfly && r.Reason.StartsWith("捕虫网捕捉")).All(r=>r.Gate==ProgressionSources.BossTier(live,NPCID.Plantera)),
                "native Prismatic Lacewing capture keeps Plantera gate without overriding earlier loaded-mod sources");
            check(live.GetTierIndex(ItemKey.Crafted(ItemID.AshWood))==0 && live.GetTierIndex(ItemKey.Crafted(ItemID.SpicyPepper))==0,
                "natural ash trees and shaken peppers unlock their early ash-wood recipes");
            if(Terraria.ModLoader.ModLoader.TryGetMod("FargowiltasSouls", out var souls) && souls.TryFind<Terraria.ModLoader.ModItem>("WoodEnchant", out var wood))
                check(live.GetTierIndex(ItemKey.Crafted(wood.Type)) == 0 && live.GetExplanation(ItemID.Squirrel).Contains("捕捉"),
                    "real Wood Enchantment inherits natural Squirrel capture instead of Trojan Squirrel boss loot");
            CheckFishing(check, live, sourceAnchors);
            CheckAuditGaps(check,live);
            CheckOtherSources(check,live);
            CheckContinuedAudit(check,live);
            check(sourceAnchors.GetValueOrDefault(ItemKey.Crafted(ItemID.LifeCrystal),-1)==0,
                "natural life crystals have a world-generation source before every boss independently of crafting recipes");
            check(sourceAnchors.TryGetValue(ItemKey.Crafted(ItemID.Ectoplasm),out int ectoplasm)
                && ectoplasm == ProgressionSources.BossTier(live,NPCID.Plantera),
                "actual dungeon-spirit drop records anchor ectoplasm to the Plantera world change");
            check(sourceAnchors.GetValueOrDefault(ItemKey.Crafted(ItemID.SoulofNight),-1)==ProgressionSources.BossTier(live,NPCID.WallofFlesh)
                && sourceAnchors.GetValueOrDefault(ItemKey.Crafted(ItemID.SoulofLight),-1)==ProgressionSources.BossTier(live,NPCID.WallofFlesh),
                "actual global soul drop conditions unlock light and night souls only after the Wall of Flesh");
            var routes = ProgressionSources.MiningRoutes(live);
            check(routes.Exists(r=>r.Item==ItemID.CobaltOre && r.Gate==ProgressionSources.BossTier(live,NPCID.WallofFlesh))
                && routes.Exists(r=>r.Item==ItemID.ChlorophyteOre && r.Pick==200),
                "actual ore routes retain both world-generation and mining-power requirements");
            if(Terraria.ModLoader.ModLoader.HasMod("CalamityMod"))
                check(routes.Single(r=>r.Item==ItemID.MythrilOre).Gate>=0,
                    "loaded Calamity ore-rework configuration is readable without mutating its settings");
        }
        if(live != null)
        {
            check(live.GetTierIndex(ItemKey.Crafted(ItemID.LifeCrystal))==0 && live.GetExplanation(ItemID.LifeCrystal).Contains("自然"),
                "real life-crystal sorting chooses natural world generation over its later crafting or boss-drop alternatives");
            check(live.GetTierIndex(ItemKey.Crafted(ItemID.MagicMirror))==0 && live.GetExplanation(ItemID.MagicMirror).Contains("宝箱"),
                "real early chest loot is ranked by exploration instead of a later crafting route");
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
            graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.FrogLeg),Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Ruby));
            graph.AnchorItem(ItemKey.Crafted(ItemID.FrogLeg),"start");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==0,
                "an early fishing material lowers dependent recipe stages instead of being only a tooltip correction");
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
            graph=Graph(Recipe.Create(ItemID.MoltenPickaxe).AddIngredient(ItemID.Ruby),Recipe.Create(ItemID.ObsidianSkull).AddIngredient(ItemID.Hellstone));
            graph.AnchorItem(ItemKey.Crafted(ItemID.CopperPickaxe),"start");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Hellstone))==1 && graph.GetTierIndex(ItemKey.Crafted(ItemID.ObsidianSkull))==1
                && graph.GetExplanation(ItemID.Hellstone).Contains(Lang.GetItemNameValue(ItemID.MoltenPickaxe)),
                "a boss material unlocks a stronger pick, then a mineable ore, then dependent recipes with a tool explanation");
            graph=Graph(Recipe.Create(ItemID.MoltenPickaxe).AddIngredient(ItemID.Hellstone));
            graph.AnchorItem(ItemKey.Crafted(ItemID.CopperPickaxe),"start");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Hellstone))<0,"an ore and its only capable pick cannot bootstrap each other's acquisition stage");
            graph=Graph(); graph.AnchorItem(ItemKey.Crafted(ItemID.Ectoplasm),"bc_fixture_wall");
            var priorNpc=graph.Tiers[1]; var priorDowned=priorNpc.IsDowned;
            graph.ApplyOrder(new[]{"bc_fixture_wall","bc_fixture_eye"});
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Ectoplasm))==1 && ReferenceEquals(graph.Tiers[2],priorNpc)
                && ReferenceEquals(graph.Tiers[2].IsDowned,priorDowned) && graph.Tiers[2].NpcIds.Contains(NPCID.EyeofCthulhu),
                "reordering remaps explicit anchors and preserves original NPC records and defeat callbacks");
            check(graph.GetIntervalName(1).Contains("血肉墙后") && graph.GetIntervalName(1).Contains("克苏鲁之眼前")
                && graph.GetIntervalName(-1)=="阶段未知","stage labels describe explicit after/before intervals and unknown remains distinct");
            graph=Graph(Recipe.Create(ItemID.LifeCrystal).AddIngredient(ItemID.Ruby).AddCondition(Condition.Hardmode));
            graph.AnchorItem(ItemKey.Crafted(ItemID.LifeCrystal),"start");
            graph.AnchorItem(ItemKey.Crafted(ItemID.Ruby),"start");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.LifeCrystal))==0 && !graph.HasUnlocks(1) && graph.HasUnlocks(2),
                "a naturally available item stays early while its later newly available recipe still makes a meaningful checkpoint");
            check(graph.GetDisplayTierIndex(1)==0 && graph.GetIntervalName(0).Contains("血肉墙前")
                && !graph.GetIntervalName(0).Contains("克苏鲁之眼前") && graph.Tiers.Count==3,
                "empty checkpoints disappear from labels and next-checkpoint text without deleting the editable boss timeline");
            graph=Graph();graph.AnchorItem(ItemKey.Crafted(ItemID.Ruby),"start");
            graph.AnchorItem(ItemKey.Crafted(ItemID.CobaltShield),"bc_fixture_eye");
            check(graph.HasUnlocks(1) && !graph.HasUnlocks(2) && graph.GetDisplayTierIndex(1)==1,
                "a new direct drop retains its own phase even when it unlocks no crafting recipe");
            graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood));
            graph.AnchorItem(ItemKey.Crafted(ItemID.Ruby),"start");
            check(!graph.HasUnlocks(1) && !graph.HasUnlocks(2) && graph.GetIntervalName(0)=="Boss 前",
                "a timeline with no later unlocks omits every irrelevant boss instead of advertising an unrelated next boss");
            CheckContainers(check,Graph);
            graph=Graph();graph.AnchorItem(ItemKey.Crafted(ItemID.SiltBlock),"start");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.AmberMosquito))<0,
                "a processing input alone cannot bootstrap extractinator rewards without a machine");
            graph=Graph(Recipe.Create(ItemID.Extractinator).AddIngredient(ItemID.Ruby),
                Recipe.Create(ItemID.Torch).AddIngredient(ItemID.AmberMosquito));
            graph.AnchorItem(ItemKey.Crafted(ItemID.SiltBlock),"start");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.AmberMosquito))==1 && graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==1,
                "a later machine unlock propagates through extraction into dependent recipes");
            graph=Graph(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.AegisCrystal));
            graph.AnchorItem(ItemKey.Crafted(ItemID.LifeCrystal),"bc_fixture_wall");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.AegisCrystal))==2 && graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==2,
                "direct shimmer routes inherit the input stage and propagate into later crafting");
            var drops=(Dictionary<int,List<DropRecord>>)typeof(GameDataIndex).GetField("NpcDrops",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);
            bool had=drops.TryGetValue(NPCID.EyeofCthulhu,out var prior);
            try
            {
                drops[NPCID.EyeofCthulhu]=new(){new DropRecord(ItemID.Torch,NPCID.EyeofCthulhu,0,1,1,1,Array.Empty<Terraria.GameContent.ItemDropRules.IItemDropRuleCondition>())};
                graph=Graph();
                typeof(ProgressionGraph).GetMethod("AnchorItemsFromBosses",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(graph,null);
                check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==1,"BossChecklist bc identifiers anchor real drop records by NPC identity");
                drops[NPCID.EyeofCthulhu]=new(){new DropRecord(ItemID.Torch,NPCID.EyeofCthulhu,0,1,1,1,
                    new Terraria.GameContent.ItemDropRules.IItemDropRuleCondition[]{new Terraria.GameContent.ItemDropRules.Conditions.IsHardmode()})};
                graph=Graph();graph.Tiers[1].LootItems.Add(ItemID.Torch);
                typeof(ProgressionGraph).GetMethod("AnchorItemsFromBosses",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(graph,null);
                check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))==2,
                    "BossChecklist loot metadata cannot bypass the actual drop's additional Hardmode gate");
                drops[NPCID.EyeofCthulhu]=new(){new DropRecord(ItemID.Torch,NPCID.EyeofCthulhu,0,1,1,1,
                    new Terraria.GameContent.ItemDropRules.IItemDropRuleCondition[]{new OpaqueDropCondition()})};
                graph=Graph();graph.Tiers[1].LootItems.Add(ItemID.Torch);
                typeof(ProgressionGraph).GetMethod("AnchorItemsFromBosses",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(graph,null);
                check(graph.GetTierIndex(ItemKey.Crafted(ItemID.Torch))<0,
                    "unrecognised drop conditions remain unknown without executing their predicate or trusting flattened metadata");
            }
            finally {if(had)drops[NPCID.EyeofCthulhu]=prior;else drops.Remove(NPCID.EyeofCthulhu);}
        }
        finally {Main.recipe=recipes;Recipe.numRecipes=count;}
    }
    private static void CheckContinuedAudit(Action<bool,string> check, ProgressionGraph graph)
    {
        int Tier(int type) => graph.GetTierIndex(ItemKey.Crafted(type));
        foreach (int item in new[] { ItemID.MeteoriteBar, ItemID.Meteorite, ItemID.MeteorHelmet, ItemID.SpaceGun,
            ItemID.NaturesGift, ItemID.JungleRose, ItemID.Sunflower, ItemID.Book,
            ItemID.WaterCandle, ItemID.Sandstone, ItemID.HardenedSand, ItemID.HellfireArrow,
            ItemID.Flare, ItemID.BlueFlare, ItemID.ThrowingKnife, ItemID.Gi, ItemID.Katana, ItemID.WaterGun, ItemID.BlandWhip, ItemID.GardenGnome, ItemID.RainHat, ItemID.RainCoat, ItemID.UmbrellaHat,
            ItemID.ShadowScale, ItemID.TissueSample, ItemID.NightmarePickaxe, ItemID.DeathbringerPickaxe,
            ItemID.Hellstone, ItemID.HellstoneBar, ItemID.WaterBolt, ItemID.GoldenKey, ItemID.ShadowKey,
            ItemID.Muramasa, ItemID.DarkLance, ItemID.AlchemyTable, ItemID.WaterBucket, ItemID.ShimmerBlock })
            check(Tier(item)==0, "continued Wiki audit retains a legitimate pre-boss exploration route: " + ItemID.Search.GetName(item));
        int hardmode=ProgressionSources.BossTier(graph,NPCID.WallofFlesh);
        foreach (int item in new[] { ItemID.MechanicalEye, ItemID.MechanicalWorm, ItemID.MechanicalSkull, ItemID.Uzi, ItemID.RodofDiscord, ItemID.LightningBug })
            check(Tier(item)==hardmode, "continued Wiki audit preserves the real Hardmode requirement: " + ItemID.Search.GetName(item));
        if (Terraria.ModLoader.ModLoader.TryGetMod("FargowiltasSouls", out var souls)
            && souls.TryFind<Terraria.ModLoader.ModItem>("NinjaEnchant", out var ninja))
            check(Tier(ninja.Type)==ProgressionSources.BossTier(graph,NPCID.KingSlime),
                "natural throwing knives and the traveling merchant Gi stop delaying Ninja Enchantment until Blood Moon");
        if (Terraria.ModLoader.ModLoader.TryGetMod("CalamityMod",out var mod) && mod.Version==new Version(2,2,4))
        {
            foreach(string name in new[]{"WulfrumMetalScrap","EnergyCore","StormlionMandible","Navystone","SeaPrism","PrismShard","Voidstone"})
            {
                bool found=mod.TryFind<Terraria.ModLoader.ModItem>(name,out var item);
                check(found && Tier(item.Type)==0,"audited Calamity exploration source is pre-boss: "+name);
            }
            int Boss(params string[] names)=>ProgressionSources.BossTier(graph,names.Select(n=>mod.Find<Terraria.ModLoader.ModNPC>(n).Type).ToArray());
            foreach(string name in new[]{"Lumenyl","DepthCells"})
                check(Tier(mod.Find<Terraria.ModLoader.ModItem>(name).Type)==Boss("Leviathan","Anahita"),
                    "Calamity abyss conditional resources retain the installed Leviathan gate: "+name);
            check(Tier(mod.Find<Terraria.ModLoader.ModItem>("AerialiteOre").Type)==Boss("HiveMind","PerforatorHive"),
                "Aerialite's infused ore and slime sources beat the later cobalt shimmer alternative");
            foreach(int item in new[]{ItemID.Frostbrand,ItemID.IceBow,ItemID.FlowerofFrost})
                check(Tier(item)==hardmode,"Calamity ordinary-world ice mimic reward retains its real Hardmode source: "+item);
            check(Tier(mod.Find<Terraria.ModLoader.ModItem>("BloodOrb").Type)==ProgressionSources.BossTier(graph,NPCID.EyeofCthulhu),
                "powder transformation provides blood orbs after the Eye without requiring the Blood Moon event");
            var mining=ProgressionCalamitySources.MiningRoutes(graph);
            check(mining.Exists(r=>r.Item==mod.Find<Terraria.ModLoader.ModItem>("CryonicOre").Type && r.Gate==Boss("Cryogen")),
                "Cryogen ore generation does not inherit the distinct Cryo Slime two-mechanical-boss spawn gate");
            check(Tier(mod.Find<Terraria.ModLoader.ModItem>("ScoriaOre").Type)<=ProgressionSources.BossTier(graph,NPCID.Golem)
                && mining.Exists(r=>r.Item==mod.Find<Terraria.ModLoader.ModItem>("ScoriaOre").Type && r.Pick==210),
                "Scoria has a Golem enemy alternative while its natural mining route still requires actual pick power");
        }
        var mostSpecific=graph.Tiers.Where(t=>t.NpcIds.Contains(NPCID.DD2Betsy)).OrderBy(t=>t.NpcIds.Count).ThenBy(t=>t.Index).FirstOrDefault();
        if(mostSpecific!=null)
            check(ProgressionSources.BossTier(graph,NPCID.DD2Betsy)==mostSpecific.Index,
                "a boss-specific checkpoint takes priority over an earlier multi-wave event containing the same NPC");
        check(ProgressionSources.ConditionTier(graph,Condition.NightOrEclipse)==0,
            "an ordinary night alternative does not inherit the Solar Eclipse stage");
        check(ProgressionSources.ConditionTier(graph,Condition.Eclipse)==Math.Max(hardmode,ProgressionSources.EventTier(graph,"Eclipse")),
            "an explicit eclipse requirement retains both event and Hardmode gates");
        var incomplete=new ProgressionGraph(new RecipeIndex());
        incomplete.Tiers.Add(new(0,"start","start","fixture"));
        var mech=new ProgressionTier(1,"mech","mech","fixture"); mech.NpcIds.Add(NPCID.TheDestroyer); incomplete.Tiers.Add(mech);
        check(ProgressionSources.ConditionTier(incomplete,Condition.DownedMechBossAll)<0,
            "an ALL condition cannot discard a missing mechanical boss checkpoint");
        var dummy=new DropRecord(ItemID.MechanicalEye,NPCID.BlueSlime,0,1,1,1,
            new Terraria.GameContent.ItemDropRules.IItemDropRuleCondition[] { new Terraria.GameContent.ItemDropRules.Conditions.MechanicalBossesDummyCondition() });
        check(ProgressionSources.DropTier(graph,dummy)==hardmode,
            "mechanical spawner report metadata preserves the Hardmode gate implemented by its parent drop rule");
    }
    private static void CheckOldOnesArmyGates(Action<bool,string> check)
    {
        ProgressionTier Add(ProgressionGraph graph, int index, string id, int npc = 0)
        {
            var tier = new ProgressionTier(index, id, id, "fixture");
            if (npc > 0) tier.NpcIds.Add(npc);
            graph.Tiers.Add(tier);
            return tier;
        }
        var earlyEvent = new ProgressionGraph(new RecipeIndex());
        Add(earlyEvent, 1, "bc_Terraria OldOnesArmy");
        Add(earlyEvent, 2, "darkMage", NPCID.DD2DarkMageT1);
        Add(earlyEvent, 3, "evilBoss", NPCID.EaterofWorldsHead);
        check(ProgressionSources.ConditionTier(earlyEvent, Condition.DownedOldOnesArmyT1) == 3,
            "moving Old One's Army before the evil boss cannot bypass the Tavernkeep unlock");

        var thirdOnly = new ProgressionGraph(new RecipeIndex());
        Add(thirdOnly, 1, "bc_Terraria OldOnesArmy");
        var evil = Add(thirdOnly, 2, "evilBoss", NPCID.BrainofCthulhu);
        var wall = Add(thirdOnly, 3, "wall", NPCID.WallofFlesh);
        Add(thirdOnly, 4, "golem", NPCID.Golem);
        Add(thirdOnly, 5, "betsy", NPCID.DD2Betsy);
        check(ProgressionSources.ConditionTier(thirdOnly, Condition.DownedOldOnesArmyT1) == 5
            && ProgressionSources.ConditionTier(thirdOnly, Condition.DownedOldOnesArmyT2) == 5,
            "completing Old One's Army tier three also unlocks tier one and two without their separate checkpoints");
        check(ProgressionSources.ConditionTier(thirdOnly, Condition.DownedOldOnesArmyAny) == 5,
            "any completed Old One's Army difficulty remains known when lower-difficulty routes are unavailable");
        wall.Index = 8;
        check(ProgressionSources.ConditionTier(thirdOnly, Condition.DownedOldOnesArmyT3) == 8,
            "moving Golem and Betsy earlier cannot bypass the Hardmode requirement of tier three");
        thirdOnly.Tiers.Remove(evil);
        check(ProgressionSources.ConditionTier(thirdOnly, Condition.DownedOldOnesArmyAny) < 0,
            "all Old One's Army completion routes remain unknown when their shared evil-boss gate is missing");
    }
    private static void CheckAuditGaps(Action<bool,string> check,ProgressionGraph graph)
    {
        int Tier(int type)=>graph.GetTierIndex(ItemKey.Crafted(type));
        int hardmode=ProgressionSources.BossTier(graph,NPCID.WallofFlesh);
        foreach(int type in new[]{ItemID.SoulofFlight,ItemID.MedusaHead,ItemID.TurtleShell,ItemID.FrozenTurtleShell,ItemID.DemonHeart})
            check(Tier(type)==hardmode,"continued acquisition audit resolves the actual Hardmode source: "+ItemID.Search.GetName(type));
        check(Tier(ItemID.FireFeather)==ProgressionSources.BossTier(graph,NPCID.Retinazer,NPCID.TheDestroyer,NPCID.SkeletronPrime),
            "Red Devil loot retains the any-mechanical-boss spawn requirement");
        int moonlord=ProgressionSources.BossTier(graph,NPCID.MoonLordCore);
        foreach(int type in new[]{ItemID.WingsSolar,ItemID.WingsVortex,ItemID.WingsNebula,ItemID.WingsStardust})
            check(Tier(type)==moonlord,"continued audit propagates proven material sources into lunar wings: "+ItemID.Search.GetName(type));
    }
    private sealed class OpaqueDropCondition : Terraria.GameContent.ItemDropRules.IItemDropRuleCondition
    {
        public bool CanDrop(Terraria.GameContent.ItemDropRules.DropAttemptInfo info) =>
            throw new InvalidOperationException("Acquisition queries must never execute a drop predicate");
        public bool CanShowItemDropInUI()=>true;
        public string GetConditionDescription()=>"Unknown regression predicate";
    }
    private static void CheckOtherSources(Action<bool,string> check,ProgressionGraph graph)
    {
        int Tier(int type)=>graph.GetTierIndex(ItemKey.Crafted(type));
        check(GameDataIndex.GetNpcDropRecords(NPCID.Pinky).Any(d=>d.ItemType==ItemID.PinkGel),
            "negative-net-ID Pinky loot is indexed independently of regular blue-slime drops");
        foreach(int type in new[]{ItemID.SharkFin,ItemID.PinkGel,ItemID.VileMushroom,ItemID.ViciousMushroom,
            ItemID.JungleGrassSeeds,ItemID.AmberMosquito,ItemID.AegisCrystal,ItemID.ArcaneCrystal,
            ItemID.Ambrosia,ItemID.GummyWorm,ItemID.GalaxyPearl})
            check(Tier(type)==0,"verified non-fishing acquisition remains pre-boss in the actual pack: "+ItemID.Search.GetName(type));
        int hardmode=ProgressionSources.BossTier(graph,NPCID.WallofFlesh);
        foreach(int type in new[]{ItemID.JungleKey,ItemID.CorruptionKey,ItemID.CrimsonKey,ItemID.FrozenKey,ItemID.HallowedKey,ItemID.DungeonDesertKey})
            check(Tier(type)==hardmode,"biome key drops require Hardmode without inheriting the later chest-opening gate: "+ItemID.Search.GetName(type));
        int mech=ProgressionSources.BossTier(graph,NPCID.TheDestroyer,NPCID.Retinazer,NPCID.SkeletronPrime);
        check(Tier(ItemID.LifeFruit)==mech && Tier(ItemID.AegisFruit)==mech,
            "life fruit natural growth after any mechanical boss also unlocks its direct shimmer output");
        int goblins=ProgressionSources.EventTier(graph,"GoblinArmy");
        check(Tier(ItemID.TinkerersWorkshop)==goblins && Tier(ItemID.RocketBoots)==goblins,
            "Goblin Tinkerer stock follows the editable Goblin Army event checkpoint");
        check(ProgressionSources.MiningRoutes(graph).Exists(r=>r.Item==ItemID.Meteorite && r.Gate==ProgressionSources.BossTier(graph,NPCID.EaterofWorldsHead,NPCID.BrainofCthulhu)),
            "meteorite falling retains its evil-boss gate even when chest bars provide a separate early route");
        check(Tier(ItemID.ShadowFlameKnife)>=hardmode,
            "Hardmode Goblin Summoner drops cannot inherit the pre-Hardmode invasion checkpoint");
        check(Tier(ItemID.SanguineStaff)>=hardmode && Tier(ItemID.DD2BetsyBow)>=ProgressionSources.BossTier(graph,NPCID.Golem),
            "event member NPCs keep their own boss and spawn gates instead of inheriting the first event tier");
        int moonlord=ProgressionSources.BossTier(graph,NPCID.MoonLordCore);
        check(Tier(ItemID.RodOfHarmony)==ProgressionCalamitySources.RodOfHarmonyGate(graph) && Tier(ItemID.Clentaminator2)==moonlord
            && Tier(ItemID.BottomlessShimmerBucket)==moonlord,
            "special shimmer upgrades retain vanilla locks and Calamity's two endgame boss requirements");
        var routes=ProgressionWorldSources.Routes(graph);
        check(routes.Exists(r=>r.Output==ItemID.AmberMosquito && r.Inputs.Contains(ItemID.Extractinator) && r.Inputs.Contains(ItemID.SiltBlock)),
            "extractinator progression requires both the processing material and a machine");
    }
    private static void CheckFishing(Action<bool,string> check, ProgressionGraph graph, Dictionary<ItemKey,int> anchors)
    {
        foreach (int type in new[] { ItemID.BalloonPufferfish, ItemID.ZephyrFish, ItemID.ReaverShark,
            ItemID.ArmoredCavefish, ItemID.Honeyfin, ItemID.Obsidifish, ItemID.LavaCrate,
            ItemID.FloatingIslandFishingCrate, ItemID.GoldenFishingRod, ItemID.GoldenBugNet,
            ItemID.BottomlessBucket, ItemID.SuperAbsorbantSponge, ItemID.BottomlessHoneyBucket })
            check(anchors.GetValueOrDefault(ItemKey.Crafted(type),-1)==0 && graph.GetTierIndex(ItemKey.Crafted(type))==0,
                "verified pre-boss fishing or Angler route remains early in the loaded pack: " + ItemID.Search.GetName(type));
        int hardmode=ProgressionSources.BossTier(graph,NPCID.WallofFlesh);
        foreach (int type in new[] { ItemID.CrystalSerpent, ItemID.Toxikarp, ItemID.Bladetongue,
            ItemID.ScalyTruffle, ItemID.ObsidianSwordfish, ItemID.GreenJellyfish,
            ItemID.HotlineFishingHook, ItemID.FinWings, ItemID.GoldenCrateHard })
            check(anchors.GetValueOrDefault(ItemKey.Crafted(type),-1)==hardmode,
                "verified fishing source keeps its explicit Hardmode gate: " + ItemID.Search.GetName(type));
        int skeletron=ProgressionSources.BossTier(graph,NPCID.SkeletronHead);
        check(anchors.GetValueOrDefault(ItemKey.Crafted(ItemID.DungeonFishingCrate),-1)==skeletron
            && anchors.GetValueOrDefault(ItemKey.Crafted(ItemID.DungeonFishingCrateHard),-1)==Math.Max(hardmode,skeletron),
            "Dungeon crates respect Skeletron and Hardmode together rather than treating every biome crate as pre-boss");
        check(graph.GetTierIndex(ItemKey.Crafted(ItemID.LuckyHorseshoe))==0
            && graph.GetTierIndex(ItemKey.Crafted(ItemID.SailfishBoots))==0,
            "new fishing-crate sources feed the real container index and preserve early exploration alternatives");
        var fishing = new HashSet<int>();
        ProgressionFishingSources.Seed(graph, (type, tier, reason) => fishing.Add(type));
        check(fishing.Contains(ItemID.FishingSeaweed) && !fishing.Contains(ItemID.Seaweed),
            "fishing trash uses the actual catch identity rather than the similarly named turtle pet");
        if (Terraria.ModLoader.ModLoader.TryGetMod("CalamityMod",out var calamity) && calamity.Version==new Version(2,2,4))
            check(anchors.GetValueOrDefault(ItemKey.Crafted(ItemID.Sundial),-1)==0
                && graph.GetTierIndex(ItemKey.Crafted(ItemID.Sundial))==0,
                "installed Calamity 2.2.4 makes Enchanted Sundial an early guaranteed Angler reward without importing obsolete Hotline gates");
        bool hard=Main.hardMode, downed=NPC.downedBoss3;
        ProgressionSources.Anchor(graph,new Dictionary<ItemKey,int>());
        check(Main.hardMode==hard && NPC.downedBoss3==downed,
            "fishing acquisition lookup does not run catches, quests or mutate world progression");
    }
    private static void CheckContainers(Action<bool,string> check,Func<Recipe[],ProgressionGraph> graphFactory)
    {
        var records=(Dictionary<int,List<DropRecord>>)typeof(GameDataIndex).GetField("ContainerDrops",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null);
        bool had=records.TryGetValue(ItemID.LockBox,out var original);
        try
        {
            records[ItemID.LockBox]=new(){new DropRecord(ItemID.IronBar,0,ItemID.LockBox,1,1,1,
                Array.Empty<Terraria.GameContent.ItemDropRules.IItemDropRuleCondition>())};
            var graph=graphFactory(new[]{Recipe.Create(ItemID.CobaltPickaxe).AddIngredient(ItemID.IronBar)});
            graph.AnchorItem(ItemKey.Crafted(ItemID.LockBox),"start");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.IronBar))<0,
                "a locked container cannot unlock its contents before its required key is obtainable");
            graph.AnchorItem(ItemKey.Crafted(ItemID.GoldenKey),"bc_fixture_wall");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.IronBar))==2
                && graph.GetTierIndex(ItemKey.Crafted(ItemID.CobaltPickaxe))==2,
                "container, key and contents propagate their earliest combined route into recipes and tools");
            graph=graphFactory(new[]{Recipe.Create(ItemID.IronBar).AddIngredient(ItemID.Wood)});
            graph.AnchorItem(ItemKey.Crafted(ItemID.LockBox),"bc_fixture_wall");
            graph.AnchorItem(ItemKey.Crafted(ItemID.GoldenKey),"bc_fixture_wall");
            check(graph.GetTierIndex(ItemKey.Crafted(ItemID.IronBar))==0,
                "an earlier crafting route wins over a later valid chest-opening route");
        }
        finally {if(had) records[ItemID.LockBox]=original;else records.Remove(ItemID.LockBox);}
    }

}
