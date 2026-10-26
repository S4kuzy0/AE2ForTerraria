using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Progression;

/// <summary>Declared exploration routes. Being placeable does not prove that an item generates naturally.</summary>
internal static class ProgressionWorldSources
{
    private static readonly int[] NaturalItems =
    {
        ItemID.LifeCrystal, ItemID.Cobweb, ItemID.Mushroom, ItemID.GlowingMushroom, ItemID.JungleSpores,
        ItemID.Daybloom, ItemID.DaybloomSeeds, ItemID.Blinkroot, ItemID.BlinkrootSeeds,
        ItemID.Moonglow, ItemID.MoonglowSeeds, ItemID.Deathweed, ItemID.DeathweedSeeds,
        ItemID.Waterleaf, ItemID.WaterleafSeeds, ItemID.Fireblossom, ItemID.FireblossomSeeds,
        ItemID.Shiverthorn, ItemID.ShiverthornSeeds, ItemID.Cactus, ItemID.Coral, ItemID.Acorn,
        ItemID.FallenStar, ItemID.SiltBlock, ItemID.SlushBlock, ItemID.DesertFossil,
        ItemID.VileMushroom, ItemID.ViciousMushroom, ItemID.JungleGrassSeeds,
        ItemID.Apple, ItemID.Apricot, ItemID.Grapefruit, ItemID.Lemon, ItemID.Peach,
        ItemID.Cherry, ItemID.Plum, ItemID.BlackCurrant, ItemID.Elderberry, ItemID.BloodOrange,
        ItemID.Rambutan, ItemID.Mango, ItemID.Pineapple, ItemID.Banana, ItemID.Coconut,
        ItemID.AshWood, ItemID.SpicyPepper
    };
    private static readonly int[] EarlyChestItems =
    {
        ItemID.MagicMirror, ItemID.CloudinaBottle, ItemID.BandofRegeneration, ItemID.HermesBoots,
        ItemID.Aglet, ItemID.ClimbingClaws, ItemID.ShoeSpikes, ItemID.Radar, ItemID.Umbrella,
        ItemID.Spear, ItemID.WoodenBoomerang, ItemID.Blowpipe, ItemID.WandofSparking,
        ItemID.LavaCharm, ItemID.IceMirror, ItemID.IceBlade, ItemID.IceBoomerang,
        ItemID.BlizzardinaBottle, ItemID.FlurryBoots, ItemID.IceSkates, ItemID.SnowballCannon,
        ItemID.IceMachine, ItemID.Extractinator, ItemID.FlareGun,
        ItemID.PharaohsMask, ItemID.PharaohsRobe, ItemID.SandstorminaBottle, ItemID.FlyingCarpet,
        ItemID.Starfury, ItemID.LuckyHorseshoe, ItemID.ShinyRedBalloon, ItemID.CreativeWings,
        ItemID.FeralClaws, ItemID.AnkletoftheWind, ItemID.StaffofRegrowth, ItemID.Boomstick,
        ItemID.FlowerBoots, ItemID.LivingWoodWand, ItemID.LeafWand,
        ItemID.Flipper, ItemID.Trident, ItemID.WaterWalkingBoots, ItemID.BreathingReed
    };
    internal static void Seed(ProgressionGraph graph, Action<int,int,string> add)
    {
        foreach(int type in NaturalItems) add(type,0,"世界自然生成 / 采集");
        foreach(int type in EarlyChestItems) add(type,0,"世界生成的地表、地下或生物群系宝箱");
        ProgressionFishingSources.Seed(graph, add);
        ProgressionExplorationSources.Seed(graph, add);
        ProgressionTownSources.Seed(graph, add);
    }
    internal sealed record AcquisitionRoute(int Output,int Gate,int[] Inputs,string Reason,
        IReadOnlyList<Condition> Conditions = null, DropRecord Drop = null);
    internal static List<AcquisitionRoute> Routes(ProgressionGraph graph)
    {
        var result=new List<AcquisitionRoute>();
        AddCritterRoutes(graph, result);
        result.AddRange(ProgressionExplorationSources.Routes(graph));
        result.AddRange(ProgressionTownSources.Routes(graph));
        result.AddRange(ProgressionCalamitySources.Routes(graph));
        AddMetalDecraftingRoutes(graph, result);
        foreach(var container in Containers(graph))
            foreach(var route in container.Value)
                result.Add(new(route.Output,route.Gate,route.Key>0 ? new[]{container.Key,route.Key} : new[]{container.Key},"开启容器",Drop:route.Drop));

        // Direct vanilla transmutations only. Decrafting and arbitrary mod
        // CanShimmer callbacks cannot be proven from the transformation table.
        for(int input=1;input<ItemID.Count;input++)
        {
            int output=ItemID.Sets.ShimmerTransformToItem[input];
            if(output>0) result.Add(new(output,0,new[]{input},"微光直接转换"));
        }
        int moonlord=ProgressionSources.BossTier(graph,NPCID.MoonLordCore);
        foreach(var pair in new[]{(ItemID.RodofDiscord,ItemID.RodOfHarmony),(ItemID.Clentaminator,ItemID.Clentaminator2),
            (ItemID.BottomlessBucket,ItemID.BottomlessShimmerBucket),(ItemID.BottomlessShimmerBucket,ItemID.BottomlessBucket)})
        {
            result.RemoveAll(r=>r.Reason=="微光直接转换" && r.Inputs[0]==pair.Item1);
            int gate = pair.Item1 == ItemID.RodofDiscord ? ProgressionCalamitySources.RodOfHarmonyGate(graph) : moonlord;
            string reason = pair.Item1 == ItemID.RodofDiscord && Terraria.ModLoader.ModLoader.HasMod("CalamityMod")
                ? "灾厄微光转换：至尊灾厄与星流巨械全部击败后" : "月亮领主后微光转换";
            if (gate >= 0) result.Add(new(pair.Item2,gate,new int[]{pair.Item1},reason));
        }
        int[] extractOutputs={ItemID.CopperOre,ItemID.TinOre,ItemID.IronOre,ItemID.LeadOre,
            ItemID.SilverOre,ItemID.TungstenOre,ItemID.GoldOre,ItemID.PlatinumOre,ItemID.Amethyst,
            ItemID.Topaz,ItemID.Sapphire,ItemID.Emerald,ItemID.Ruby,ItemID.Diamond,ItemID.Amber,ItemID.AmberMosquito};
        foreach(int input in new[]{ItemID.SiltBlock,ItemID.SlushBlock,ItemID.DesertFossil})
            foreach(int machine in new[]{ItemID.Extractinator,ItemID.ChlorophyteExtractinator})
            {
                foreach(int output in extractOutputs) result.Add(new(output,0,new[]{input,machine},"提炼机处理"));
                if(input==ItemID.DesertFossil) result.Add(new(ItemID.FossilOre,0,new[]{input,machine},"沙漠化石提炼"));
            }
        foreach(int output in new[]{ItemID.WhitePearl,ItemID.BlackPearl,ItemID.PinkPearl,ItemID.ShuckedOyster})
            result.Add(new(output,0,new int[]{ItemID.Oyster},"开启牡蛎"));
        return result;
    }
    // A narrowly audited decrafting family. Use the actual first eligible recipe,
    // respect direct transformations and declared decraft restrictions, and never
    // invoke RecipeLoader.DecraftAvailable (which executes arbitrary predicates).
    private static void AddMetalDecraftingRoutes(ProgressionGraph graph, List<AcquisitionRoute> result)
    {
        foreach (int input in new[] { ItemID.CopperBar, ItemID.TinBar, ItemID.IronBar, ItemID.LeadBar,
            ItemID.SilverBar, ItemID.TungstenBar, ItemID.GoldBar, ItemID.PlatinumBar, ItemID.DemoniteBar,
            ItemID.CrimtaneBar, ItemID.MeteoriteBar, ItemID.HellstoneBar, ItemID.CobaltBar, ItemID.PalladiumBar,
            ItemID.MythrilBar, ItemID.OrichalcumBar, ItemID.AdamantiteBar, ItemID.TitaniumBar, ItemID.ChlorophyteBar })
        {
            if (ItemID.Sets.ShimmerTransformToItem[input] > 0) continue;
            foreach (int index in ItemID.Sets.CraftingRecipeIndices[input] ?? Array.Empty<int>())
            {
                if (index < 0 || index >= Recipe.numRecipes) continue;
                var recipe = Main.recipe[index];
                if (recipe == null || recipe.createItem.type != input || recipe.Disabled || recipe.DecraftDisabled) continue;
                int gate = ProgressionSources.ConditionsTier(graph,recipe.DecraftConditions,0);
                var outputs = recipe.customShimmerResults ?? recipe.requiredItem;
                // Do not generalize to furniture's bone/temple-brick exceptions.
                if (outputs.Any(item => item.type is ItemID.Bone or ItemID.LihzahrdBrick)) gate = -1;
                if (gate >= 0)
                    foreach (var output in outputs.Where(item => !item.IsAir))
                        result.Add(new(output.type, gate, new[] { input }, "金属锭微光分解（实际配方及分解条件）",recipe.DecraftConditions.ToArray()));
                // An earlier opaque recipe might shadow later recipes; stop here.
                break;
            }
        }
    }
    // Native catchItem is authoritative for the captured item, not an NPC's loot table.
    // Only native critters are covered: mod spawn/capture callbacks need separate evidence.
    private static void AddCritterRoutes(ProgressionGraph graph, List<AcquisitionRoute> result)
    {
        // Butterfly species share a single NPC type; catchItem varies with its AI variant.
        foreach (int item in new[] { ItemID.MonarchButterfly, ItemID.PurpleEmperorButterfly, ItemID.RedAdmiralButterfly,
            ItemID.UlyssesButterfly, ItemID.SulphurButterfly, ItemID.TreeNymphButterfly, ItemID.ZebraSwallowtailButterfly, ItemID.JuliaButterfly })
            result.Add(new(item, 0, new int[] { ItemID.BugNet }, "捕虫网捕捉：自然蝴蝶变体"));
        foreach (int substrate in new[] { ItemID.HallowedSeeds, ItemID.PearlsandBlock })
            result.Add(new(ItemID.LightningBug, 0, new int[] { ItemID.BugNet, substrate }, "神圣生态的荧光虫：先建立神圣环境再用捕虫网捕捉"));
        int hardmode = ProgressionSources.BossTier(graph, NPCID.WallofFlesh);
        int plantera = ProgressionSources.BossTier(graph, NPCID.Plantera);
        foreach (var npc in ContentSamples.NpcsByNetId.Values)
        {
            if (npc.type <= 0 || npc.type >= NPCID.Count || npc.catchItem <= 0
                || !NPCID.Sets.CountsAsCritter[npc.type] || npc.catchItem == ItemID.LightningBug) continue;
            int gate = npc.catchItem == ItemID.TruffleWorm ? hardmode
                : npc.catchItem == ItemID.EmpressButterfly ? plantera
                : 0;
            bool lava = npc.catchItem is ItemID.Lavafly or ItemID.HellButterfly or ItemID.MagmaSnail;
            foreach (int net in lava ? new[] { ItemID.FireproofBugNet, ItemID.GoldenBugNet } : new[] { ItemID.BugNet })
                result.Add(new(npc.catchItem, gate, new[] { net }, "捕虫网捕捉：" + npc.FullName));
        }
    }
    internal readonly record struct ContainerRoute(int Output,int Gate,int Key,DropRecord Drop);
    internal static Dictionary<int,List<ContainerRoute>> Containers(ProgressionGraph graph)
    {
        var routes = new Dictionary<int,List<ContainerRoute>>();
        var bags = graph.Tiers.Where(t=>t.TreasureBag>0).Select(t=>t.TreasureBag).ToHashSet();
        foreach(int type in ContentSamples.ItemsByType.Keys)
        {
            // Arbitrary mod containers may have opaque RightClick requirements outside their loot table.
            if(type >= ItemID.Count && !bags.Contains(type)) continue;
            foreach(var drop in GameDataIndex.GetContainerDrops(type))
            {
                int gate = ProgressionSources.DropTier(graph,drop);
                if(gate < 0) continue;
                int key = type == ItemID.LockBox ? ItemID.GoldenKey : type == ItemID.ObsidianLockbox ? ItemID.ShadowKey : 0;
                if(!routes.TryGetValue(type,out var list)) routes[type] = list = new();
                list.Add(new(drop.ItemType,gate,key,drop));
            }
        }
        return routes;
    }
}
