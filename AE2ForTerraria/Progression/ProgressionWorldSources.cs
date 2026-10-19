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
        ItemID.GoldWorm, ItemID.GoldBird, ItemID.GoldBunny, ItemID.GoldButterfly,
        ItemID.GoldFrog, ItemID.GoldGrasshopper, ItemID.GoldMouse, ItemID.GoldGoldfish,
        ItemID.GoldLadyBug, ItemID.GoldDragonfly, ItemID.GoldWaterStrider
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
    }
    internal sealed record AcquisitionRoute(int Output,int Gate,int[] Inputs,string Reason);
    internal static List<AcquisitionRoute> Routes(ProgressionGraph graph)
    {
        var result=new List<AcquisitionRoute>();
        foreach(var container in Containers(graph))
            foreach(var route in container.Value)
                result.Add(new(route.Output,route.Gate,route.Key>0 ? new[]{container.Key,route.Key} : new[]{container.Key},"开启容器"));

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
            result.Add(new(pair.Item2,moonlord,new int[]{pair.Item1},"月亮领主后微光转换"));
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
    internal readonly record struct ContainerRoute(int Output,int Gate,int Key);
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
                list.Add(new(drop.ItemType,gate,key));
            }
        }
        return routes;
    }
}
