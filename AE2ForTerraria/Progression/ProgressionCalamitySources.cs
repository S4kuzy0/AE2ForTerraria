using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Progression;

/// <summary>Exploration routes checked against Calamity 2.2.4 and its Wiki history.</summary>
internal static class ProgressionCalamitySources
{
    private static bool TryGetAuditedMod(out Mod mod) =>
        ModLoader.TryGetMod("CalamityMod", out mod) && mod.Version == new Version(2, 2, 4);

    private static int Boss(ProgressionGraph graph, Mod mod, params string[] names)
    {
        int[] types = names.Select(name => mod.TryFind<ModNPC>(name, out var npc) ? npc.Type : -1)
            .Where(type => type > 0).ToArray();
        return types.Length == 0 ? -1 : ProgressionSources.BossTier(graph, types);
    }

    private static int Both(int first, int second) => first < 0 || second < 0 ? -1 : Math.Max(first, second);

    internal static int RodOfHarmonyGate(ProgressionGraph graph)
    {
        if (!ModLoader.HasMod("CalamityMod")) return ProgressionSources.BossTier(graph, NPCID.MoonLordCore);
        if (!TryGetAuditedMod(out var mod)) return -1;
        // AdjustShimmerRequirements replaces the vanilla lock for this input.
        return Both(Boss(graph, mod, "SupremeCalamitas"), Boss(graph, mod, "AresBody", "ThanatosHead"));
    }

    internal static void Seed(ProgressionGraph graph, Action<int, int, string> add)
    {
        if (!TryGetAuditedMod(out var mod)) return;

        void Item(string name, int gate, string reason)
        {
            if (gate >= 0 && mod.TryFind<ModItem>(name, out var item)) add(item.Type, gate, reason);
        }

        void NpcDrops(string name, int spawnGate)
        {
            if (spawnGate < 0 || !mod.TryFind<ModNPC>(name, out var npc)) return;
            foreach (var drop in GameDataIndex.GetNpcDropRecords(npc.Type))
            {
                int gate = Both(spawnGate, ProgressionSources.DropTier(graph, drop));
                if (gate >= 0) add(drop.ItemType, gate, "灾厄世界 / 小怪掉落：" + Lang.GetNPCNameValue(npc.Type));
            }
        }

        // Amplifiers themselves drop cores unconditionally. Other wulfrum
        // enemies' supercharge predicates are not interpreted as generic gates.
        NpcDrops("WulfrumAmplifier", 0);
        NpcDrops("Stormlion", 0);
        NpcDrops("LuminousCorvina", 0);
        NpcDrops("AeroSlime", Boss(graph, mod, "HiveMind", "PerforatorHive"));

        int[] mechs = new[]
        {
            ProgressionSources.BossTier(graph, NPCID.TheDestroyer),
            ProgressionSources.BossTier(graph, NPCID.Retinazer),
            ProgressionSources.BossTier(graph, NPCID.SkeletronPrime)
        };
        int secondMech = mechs.Any(tier => tier < 0) ? -1 : mechs.OrderBy(tier => tier).ElementAt(1);
        NpcDrops("CryoSlime", Both(Boss(graph, mod, "Cryogen"), secondMech));

        // These particular conditional drops were read in the installed DLL.
        // Do not invoke DropHelper lambdas or generalize all such predicates.
        int leviathan = Boss(graph, mod, "Leviathan", "Anahita");
        Item("Lumenyl", leviathan, "灾厄：利维坦和阿娜希塔后，发光石首鱼掉落流明晶");
        Item("DepthCells", leviathan, "灾厄：利维坦和阿娜希塔后，深渊生物掉落深渊细胞");
        Item("ScoriaOre", ProgressionSources.BossTier(graph, NPCID.Golem), "灾厄：石巨人后，混乱刺鲀掉落熔渣矿");

        // Calamity 2.2.4 replaces these Ice Mimic rules with a predicate that
        // checks only non-statue origin and a non-Remix world.
        int hardmode = ProgressionSources.BossTier(graph, NPCID.WallofFlesh);
        if (!Main.remixWorld && hardmode >= 0)
            foreach (int item in new[] { ItemID.Frostbrand, ItemID.IceBow, ItemID.FlowerofFrost })
                add(item, hardmode, "困难模式地下雪原天然冰宝箱怪掉落（普通世界，非雕像召唤）");
    }

    internal static List<ProgressionWorldSources.AcquisitionRoute> Routes(ProgressionGraph graph)
    {
        var result = new List<ProgressionWorldSources.AcquisitionRoute>();
        if (!TryGetAuditedMod(out var mod) || !mod.TryFind<ModItem>("BloodOrb", out var orb)) return result;
        int eye = ProgressionSources.BossTier(graph, NPCID.EyeofCthulhu);
        if (eye >= 0)
            foreach (int powder in new[] { ItemID.VilePowder, ItemID.ViciousPowder })
                result.Add(new(orb.Type, eye, new[] { powder },
                    "灾厄：克苏鲁之眼后，夜间用邪恶粉末把天然猪猪变为恐惧之猪，击杀取得血珠；不要求血月"));
        return result;
    }

    internal static List<ProgressionSources.MiningRoute> MiningRoutes(ProgressionGraph graph)
    {
        var result = new List<ProgressionSources.MiningRoute>();
        if (!TryGetAuditedMod(out var mod)) return result;

        void Ore(string name, int gate, string reason)
        {
            if (gate < 0 || !mod.TryFind<ModItem>(name, out var item) || item.Item.createTile < 0) return;
            var tile = TileLoader.GetTile(item.Item.createTile);
            if (tile != null) result.Add(new(item.Type, gate, tile.MinPick, reason));
        }

        // Sunken Sea mining lost its Desert Scourge lock in 2.0.2.001.
        foreach (string name in new[] { "Navystone", "EutrophicSand", "HardenedEutrophicSand", "SeaPrism", "PrismShard" })
            Ore(name, 0, "灾厄沉沦之海天然生成，无 Boss 门槛");
        Ore("AerialiteOre", Boss(graph, mod, "HiveMind", "PerforatorHive"), "灾厄：腐巢意志或血肉宿主后，空岛天蓝矿注魔");

        // Cryogen creates the ore immediately. The two-mech requirement belongs
        // to Cryo Slime spawning, not ore generation; tools are checked separately.
        Ore("CryonicOre", Boss(graph, mod, "Cryogen"), "灾厄：极地之灵后生成寒元矿脉");
        // Scoria exists on world creation; only the tool limits mining it.
        Ore("ScoriaOre", 0, "灾厄深渊天然熔渣矿脉");
        return result;
    }
}
