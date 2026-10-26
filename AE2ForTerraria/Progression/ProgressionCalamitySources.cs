using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Systems;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Progression;

/// <summary>Current drops plus independently verified generation, spawning and conversion routes.</summary>
internal static class ProgressionCalamitySources
{
    private static bool TryGetMod(out Mod mod) => ModLoader.TryGetMod("CalamityMod", out mod);

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
        if (!TryGetMod(out var mod) || !CalamitySourceCompatibility.Supports(mod, "rod-harmony")) return -1;
        // AdjustShimmerRequirements replaces the vanilla lock for this input.
        return Both(Boss(graph, mod, "SupremeCalamitas"), Boss(graph, mod, "AresBody", "ThanatosHead"));
    }

    internal static void Seed(ProgressionGraph graph, Action<int, int, string> add)
    {
        if (!TryGetMod(out var mod)) return;

        void NpcDrops(string name, int spawnGate)
        {
            if (spawnGate < 0 || !mod.TryFind<ModNPC>(name, out var npc)
                || !CalamitySourceCompatibility.Supports(mod, "npc/" + name)) return;
            foreach (var drop in GameDataIndex.GetNpcDropRecords(npc.Type))
            {
                int gate = ProgressionSources.DropTier(graph, drop, spawnGate);
                if (gate >= 0) add(drop.ItemType, gate, "灾厄世界 / 小怪掉落：" + Lang.GetNPCNameValue(npc.Type));
            }
        }

        // Amplifiers themselves drop cores unconditionally. Other wulfrum
        // enemies' supercharge predicates are not interpreted as generic gates.
        NpcDrops("WulfrumAmplifier", 0);
        NpcDrops("Stormlion", 0);
        NpcDrops("LuminousCorvina", 0);
        NpcDrops("DevilFish", 0);
        NpcDrops("ChaoticPuffer", 0);
        NpcDrops("AeroSlime", Boss(graph, mod, "HiveMind", "PerforatorHive"));

        int[] mechs = new[]
        {
            ProgressionSources.BossTier(graph, NPCID.TheDestroyer),
            ProgressionSources.BossTier(graph, NPCID.Retinazer),
            ProgressionSources.BossTier(graph, NPCID.SkeletronPrime)
        };
        int secondMech = mechs.Any(tier => tier < 0) ? -1 : mechs.OrderBy(tier => tier).ElementAt(1);
        NpcDrops("CryoSlime", Both(Boss(graph, mod, "Cryogen"), secondMech));

        // Lumenyl, Depth Cells and Scoria now follow the registered drops above.
        // The native Ice Mimic spawn route also reads its current replacement rules.
    }

    internal static List<ProgressionWorldSources.AcquisitionRoute> Routes(ProgressionGraph graph)
    {
        var result = new List<ProgressionWorldSources.AcquisitionRoute>();
        if (!TryGetMod(out var mod) || !CalamitySourceCompatibility.Supports(mod, "blood-orb")
            || !mod.TryFind<ModItem>("BloodOrb", out var orb) || !mod.TryFind<ModNPC>("HorribleHog", out var hog)) return result;
        int eye = ProgressionSources.BossTier(graph, NPCID.EyeofCthulhu);
        foreach(var drop in GameDataIndex.GetNpcDropRecords(hog.Type).Where(drop=>drop.ItemType==orb.Type))
        {
            int dropGate = ProgressionSources.DropTier(graph,drop,eye);
            if (dropGate < 0) continue;
            foreach (int powder in new[] { ItemID.VilePowder, ItemID.ViciousPowder })
                result.Add(new(orb.Type, dropGate, new[] { powder },
                    "灾厄：克苏鲁之眼后，夜间用邪恶粉末把天然猪猪变为恐惧之猪，击杀取得血珠；不要求血月",Drop:drop));
        }
        return result;
    }

    internal static List<ProgressionSources.MiningRoute> MiningRoutes(ProgressionGraph graph)
    {
        var result = new List<ProgressionSources.MiningRoute>();
        if (!TryGetMod(out var mod)) return result;

        void Ore(string name, int gate, string reason)
        {
            if (gate < 0 || !mod.TryFind<ModItem>(name, out var item) || item.Item.createTile < 0) return;
            var tile = TileLoader.GetTile(item.Item.createTile);
            if (tile == null) return;
            var conditions = new List<AdaptivePredicate.Formula>();
            var methods = mod.GetContent<GlobalTile>().Select(global => global.GetType().GetMethod(nameof(GlobalTile.CanKillTile),
                    new[] { typeof(int), typeof(int), typeof(int), typeof(bool).MakeByRefType() }))
                .Prepend(tile.GetType().GetMethod(nameof(ModTile.CanKillTile), new[] { typeof(int), typeof(int), typeof(bool).MakeByRefType() }));
            foreach (var method in methods)
            {
                if (!AdaptiveProgressionConditions.TryRead(method, out var condition))
                { CalamitySourceCompatibility.WarnUnsupported(mod, "mining/" + name, "CanKillTile cannot be established"); return; }
                conditions.Add(condition);
            }
            int currentGate = AdaptiveProgressionConditions.Earliest(graph, conditions, gate);
            if (currentGate >= 0) result.Add(new(item.Type, currentGate, tile.MinPick, reason,conditions.ToArray()));
            else CalamitySourceCompatibility.WarnUnsupported(mod, "mining/" + name, "the current mining predicate has an unknown gate");
        }

        // Sunken Sea mining lost its Desert Scourge lock in 2.0.2.001.
        if (CalamitySourceCompatibility.Supports(mod, "sunken-sea"))
            foreach (string name in new[] { "Navystone", "EutrophicSand", "HardenedEutrophicSand", "SeaPrism", "PrismShard" })
                Ore(name, 0, "灾厄沉沦之海天然生成（按当前挖掘条件）");
        if (CalamitySourceCompatibility.Supports(mod, "aerialite-generation"))
        {
            if (CalamitySourceCompatibility.Supports(mod, "aerialite-hive"))
                Ore("AerialiteOre", Boss(graph, mod, "HiveMind"), "灾厄：腐巢意志后，空岛天蓝矿注魔");
            if (CalamitySourceCompatibility.Supports(mod, "aerialite-perforator"))
                Ore("AerialiteOre", Boss(graph, mod, "PerforatorHive"), "灾厄：血肉宿主后，空岛天蓝矿注魔");
        }

        // Cryogen creates the ore immediately. The two-mech requirement belongs
        // to Cryo Slime spawning, not ore generation; tools are checked separately.
        if (CalamitySourceCompatibility.Supports(mod, "cryonic-ore"))
            Ore("CryonicOre", Boss(graph, mod, "Cryogen"), "灾厄：极地之灵后生成寒元矿脉");
        // Scoria exists on world creation; only the tool limits mining it.
        if (CalamitySourceCompatibility.Supports(mod, "scoria-ore"))
            Ore("ScoriaOre", 0, "灾厄深渊天然熔渣矿脉");
        return result;
    }
}
