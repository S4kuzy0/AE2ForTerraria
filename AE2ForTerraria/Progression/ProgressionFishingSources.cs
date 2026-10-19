using System;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Progression;

/// <summary>Verified vanilla 1.4.4 fishing routes; rarity and quest count are not boss gates.
/// Sources and version differences are recorded in docs/acquisition-sources.md.</summary>
internal static class ProgressionFishingSources
{
    internal static void Seed(ProgressionGraph graph, Action<int, int, string> add)
    {
        void Group(int tier, string reason, params int[] items)
        {
            foreach (int type in items) add(type, tier, reason);
        }

        Group(0, "钓鱼：任意有效水域，无 Boss 门槛",
            ItemID.FrogLeg, ItemID.BalloonPufferfish, ItemID.ZephyrFish, ItemID.BombFish);
        Group(0, "钓鱼：海洋，无 Boss 门槛",
            ItemID.ReaverShark, ItemID.SawtoothShark, ItemID.Swordfish, ItemID.PinkJellyfish,
            ItemID.RedSnapper, ItemID.Tuna, ItemID.Trout, ItemID.Shrimp);
        Group(0, "钓鱼：雪原水域，无 Boss 门槛",
            ItemID.FrostDaggerfish, ItemID.FrostMinnow, ItemID.AtlanticCod);
        Group(0, "钓鱼：丛林水域，无 Boss 门槛",
            ItemID.DoubleCod, ItemID.VariegatedLardfish, ItemID.NeonTetra);
        Group(0, "钓鱼：腐化或猩红水域，无 Boss 门槛",
            ItemID.PurpleClubberfish, ItemID.Ebonkoi, ItemID.Hemopiranha, ItemID.CrimsonTigerfish);
        Group(0, "钓鱼：地下或洞穴水域，无 Boss 门槛",
            ItemID.ArmoredCavefish, ItemID.SpecularFish, ItemID.Stinkfish, ItemID.GoldenCarp,
            ItemID.Rockfish, ItemID.BlueJellyfish);
        Group(0, "钓鱼：沙漠绿洲，无 Boss 门槛", ItemID.Flounder, ItemID.RockLobster, ItemID.Oyster);
        Group(0, "钓鱼：天空水域，无 Boss 门槛", ItemID.Damselfish);
        Group(0, "钓鱼：蜂蜜，无 Boss 门槛", ItemID.Honeyfin);
        Group(0, "钓鱼：普通水域，无 Boss 门槛", ItemID.Bass, ItemID.Salmon,
            ItemID.OldShoe, ItemID.FishingSeaweed, ItemID.TinCan, ItemID.JojaCola);

        // Golden Bug Net is itself a pre-boss Angler reward, so lava bait does not require a boss.
        Group(0, "钓鱼：熔岩；可用 Boss 前渔夫奖励的金虫网捕捉熔岩鱼饵",
            ItemID.Obsidifish, ItemID.FlarefinKoi, ItemID.DemonConch,
            ItemID.BottomlessLavaBucket, ItemID.LavaAbsorbantSponge, ItemID.LavaCrate);
        Group(0, "钓鱼：对应生物群系的困难模式前宝匣，无 Boss 门槛",
            ItemID.WoodenCrate, ItemID.IronCrate, ItemID.GoldenCrate,
            ItemID.CorruptFishingCrate, ItemID.CrimsonFishingCrate, ItemID.JungleFishingCrate,
            ItemID.FloatingIslandFishingCrate, ItemID.FrozenCrate, ItemID.OasisCrate, ItemID.OceanCrate);

        int hardmode = ProgressionSources.BossTier(graph, NPCID.WallofFlesh);
        Group(hardmode, "钓鱼：困难模式水域，需血肉墙后的世界变化",
            ItemID.CrystalSerpent, ItemID.Toxikarp, ItemID.Bladetongue, ItemID.ScalyTruffle,
            ItemID.GreenJellyfish, ItemID.ChaosFish, ItemID.Prismite, ItemID.PrincessFish, ItemID.LadyOfTheLake);
        // Unlike the other lava catches, Obsidian Swordfish has an explicit hardMode check in 1.4.4.9.
        Group(hardmode, "钓鱼：困难模式熔岩（1.4.4 的显式困难模式门槛）", ItemID.ObsidianSwordfish);
        Group(hardmode, "钓鱼：困难模式的对应生物群系宝匣",
            ItemID.WoodenCrateHard, ItemID.IronCrateHard, ItemID.GoldenCrateHard,
            ItemID.CorruptFishingCrateHard, ItemID.CrimsonFishingCrateHard,
            ItemID.JungleFishingCrateHard, ItemID.FloatingIslandFishingCrateHard,
            ItemID.FrozenCrateHard, ItemID.OasisCrateHard, ItemID.OceanCrateHard,
            ItemID.HallowedFishingCrateHard, ItemID.LavaCrateHard);

        int skeletron = ProgressionSources.BossTier(graph, NPCID.SkeletronHead);
        Group(skeletron, "钓鱼：地牢，需先击败骷髅王", ItemID.DungeonFishingCrate, ItemID.AlchemyTable);
        // Both requirements must be known; a missing checkpoint must not silently become zero.
        if (hardmode >= 0 && skeletron >= 0)
            Group(Math.Max(hardmode, skeletron), "钓鱼：困难模式地牢，仍需先击败骷髅王", ItemID.DungeonFishingCrateHard);

        Group(0, "渔夫任务奖励：完成对应数量或种类的任务，无 Boss 门槛",
            ItemID.FuzzyCarrot, ItemID.AnglerHat, ItemID.AnglerVest, ItemID.AnglerPants,
            ItemID.GoldenFishingRod, ItemID.BottomlessBucket, ItemID.SuperAbsorbantSponge,
            ItemID.BottomlessHoneyBucket, ItemID.HoneyAbsorbantSponge, ItemID.GoldenBugNet,
            ItemID.FishHook, ItemID.FishMinecart, ItemID.HighTestFishingLine, ItemID.AnglerEarring,
            ItemID.TackleBox, ItemID.FishermansGuide, ItemID.WeatherRadio, ItemID.Sextant, ItemID.FishingBobber,
            ItemID.SeashellHairpin, ItemID.MermaidAdornment, ItemID.MermaidTail,
            ItemID.FishCostumeMask, ItemID.FishCostumeShirt, ItemID.FishCostumeFinskirt,
            ItemID.FishingPotion, ItemID.SonarPotion, ItemID.CratePotion);
        Group(hardmode, "渔夫任务奖励：困难模式专属，仍需完成对应任务数量",
            ItemID.HotlineFishingHook, ItemID.FinWings);
        // Audited installed hook; historical Calamity reward tables differ substantially.
        if (ModLoader.TryGetMod("CalamityMod", out var calamity) && calamity.Version == new Version(2, 2, 4))
            Group(0, "灾厄 2.2.4：渔夫完成第 26 次任务的奖励，无 Boss 门槛", ItemID.Sundial);
    }
}
