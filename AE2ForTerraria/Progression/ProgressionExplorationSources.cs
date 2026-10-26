using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Systems;
using Terraria.ID;

namespace AE2ForTerraria.Progression;

/// <summary>Audited 1.4.4 exploration routes that do not require completing a boss.</summary>
internal static class ProgressionExplorationSources
{
    internal static void Seed(ProgressionGraph graph, Action<int, int, string> add)
    {
        void Group(string reason, params int[] items)
        {
            foreach (int item in items) add(item, 0, reason);
        }

        Group("自然植物采集：森林向日葵", ItemID.Sunflower);
        Group("自然植物采集：地下丛林花朵，无 Boss 门槛", ItemID.JungleRose, ItemID.NaturesGift);
        Group("天然染料植物：对应生物群系采集，无 Boss 门槛",
            ItemID.TealMushroom, ItemID.GreenMushroom, ItemID.SkyBlueFlower, ItemID.YellowMarigold,
            ItemID.BlueBerries, ItemID.LimeKelp, ItemID.PinkPricklyPear, ItemID.OrangeBloodroot);
        Group("天然地形采集：地下沙漠沙岩与硬化沙", ItemID.Sandstone, ItemID.HardenedSand);
        Group("天然地形采集：地狱灰烬", ItemID.AshBlock);
        Group("世界宝箱：靠近地狱的深层金箱副产物，无需陨石坠落", ItemID.MeteoriteBar);
        Group("世界宝箱：地下金箱中与照明弹枪一同生成", ItemID.Flare);
        Group("世界宝箱：地表普通箱和洞穴金箱的投刀，无需血月", ItemID.ThrowingKnife);
        Group("地狱罐子掉落：无需狱石锭或困难模式", ItemID.HellfireArrow);
        Group("地牢普通未锁宝箱或地下罐子；无需击败骷髅王，深入时有守卫风险", ItemID.GoldenKey);
        Group("地牢天然摆设：入口附近可拆取，无需击败骷髅王", ItemID.Book, ItemID.WaterCandle);
        Group("地牢地下天然摆设：可冒险抢取，需躲避守卫；没有击败骷髅王的拾取锁",
            ItemID.WaterBolt, ItemID.AlchemyTable, ItemID.BewitchingTable);

        AddPartialBossDrops(graph, add);
    }

    private static void AddPartialBossDrops(ProgressionGraph graph, Action<int, int, string> add)
    {
        // Only the ordinary segment/minion rewards count. Completion bonuses
        // and opaque mod predicates must not become an unconditional early source.
        foreach (int npc in new[] { NPCID.EaterofWorldsHead, NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail, NPCID.Creeper })
            foreach (var drop in GameDataIndex.GetNpcDropRecords(npc))
            {
                bool material = npc == NPCID.Creeper
                    ? drop.ItemType is ItemID.CrimtaneOre or ItemID.TissueSample
                    : drop.ItemType is ItemID.DemoniteOre or ItemID.ShadowScale;
                if (!material || drop.Conditions.Any(condition =>
                    condition.GetType().Assembly != typeof(Terraria.GameContent.ItemDropRules.Conditions).Assembly
                    || condition.GetType().Name is "LegacyHack_IsABoss" or "LegacyHack_IsBossAndNotExpert")) continue;
                int gate = ProgressionSources.DropTier(graph, drop);
                if (gate < 0) continue;
                add(drop.ItemType, gate, npc == NPCID.Creeper
                    ? "召唤克苏鲁之脑后击杀飞眼怪；无需击败整只 Boss（保留实际掉落条件）"
                    : "召唤世界吞噬怪后击破部分体节；无需击败整只 Boss（保留实际掉落条件）");
            }
    }

    internal static List<ProgressionWorldSources.AcquisitionRoute> Routes(ProgressionGraph graph)
    {
        var routes = new List<ProgressionWorldSources.AcquisitionRoute>();
        void Add(int output, string reason, params int[] inputs) => routes.Add(new(output, 0, inputs, reason));

        foreach (int item in new[] { ItemID.MagicMissile, ItemID.Muramasa, ItemID.CobaltShield, ItemID.AquaScepter,
            ItemID.BlueMoon, ItemID.Handgun, ItemID.ShadowKey, ItemID.Valor, ItemID.BoneWelder })
            Add(item, "金钥匙开启地牢金箱；可躲避守卫提前取物，不要求击败骷髅王", ItemID.GoldenKey);
        foreach (int item in new[] { ItemID.DarkLance, ItemID.Sunfury, ItemID.Flamelash, ItemID.FlowerofFire, ItemID.HellwingBow })
            Add(item, "暗影钥匙开启地狱暗影箱", ItemID.ShadowKey);

        foreach (int item in new[] { ItemID.EbonstoneBlock, ItemID.CrimstoneBlock })
            Add(item, "用炸弹开采天然邪恶石块；不受镐力限制", ItemID.Bomb);
        foreach (int item in new[] { ItemID.GreenMoss, ItemID.BrownMoss, ItemID.RedMoss, ItemID.BlueMoss, ItemID.PurpleMoss,
            ItemID.LavaMoss, ItemID.KryptonMoss, ItemID.XenonMoss, ItemID.ArgonMoss, ItemID.VioletMoss })
            Add(item, "漆铲采集天然苔藓；品种取决于世界生成", ItemID.PaintScraper);

        Add(ItemID.WaterBucket, "空桶装取天然水", ItemID.EmptyBucket);
        Add(ItemID.LavaBucket, "空桶装取天然熔岩", ItemID.EmptyBucket);
        Add(ItemID.HoneyBucket, "空桶装取天然蜂蜜", ItemID.EmptyBucket);
        Add(ItemID.ShimmerBlock, "将水倒入天然微光池形成镒块；无需无底微光桶", ItemID.WaterBucket);
        return routes;
    }
}
