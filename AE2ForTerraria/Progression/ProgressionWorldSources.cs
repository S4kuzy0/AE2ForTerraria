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
        ItemID.FallenStar, ItemID.SiltBlock, ItemID.SlushBlock, ItemID.DesertFossil
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
        foreach(int type in new int[]{ItemID.WoodenCrate,ItemID.IronCrate,ItemID.GoldenCrate})
            add(type,0,"钓鱼：困难模式前的板条箱");
        int hardmode = ProgressionSources.BossTier(graph,NPCID.WallofFlesh);
        foreach(int type in new int[]{ItemID.WoodenCrateHard,ItemID.IronCrateHard,ItemID.GoldenCrateHard})
            add(type,hardmode,"钓鱼：困难模式板条箱");
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
