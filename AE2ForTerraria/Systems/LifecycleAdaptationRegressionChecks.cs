using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AE2ForTerraria.Acquisition;
using AE2ForTerraria.Core;
using AE2ForTerraria.Progression;
using AE2ForTerraria.UI;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

/// <summary>Uses temporary registered data and restores it; never changes saves or enabled mods.</summary>
internal static class LifecycleAdaptationRegressionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Recipe[] recipes = Main.recipe;
        int recipeCount = Recipe.numRecipes;
        var dropDatabase = Main.ItemDropsDB;
        var names = (Terraria.Localization.LocalizedText[])typeof(Lang).GetField("_itemNameCache", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null);
        var originalName = names[ItemID.Wood];
        var nameValue = typeof(Terraria.Localization.LocalizedText).GetProperty("Value")!;
        string originalText = originalName.Value;
        var rule = ItemDropRule.Common(ItemID.StoneBlock);
        try
        {
            // These inputs failed before the adaptation fixes: identical recipe object, changed
            // requirements; an ordinary-name container; and one bad entry among valid bosses.
            var recipe = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood).AddTile(TileID.WorkBenches);
            Main.recipe = new[] { recipe }; Recipe.numRecipes = 1; StorageSystem.Recipes.Build();
            string before = BrowserContent.RecipeKey(recipe);
            int blocks = Array.FindIndex(BrowserCategories.Items, category => category.Name == "物块");
            check(BrowserCategories.Matches(ItemID.WorkBench, blocks, 1), "lifecycle fixture initially recognizes its required workstation");
            recipe.requiredItem[0].stack = 7;
            recipe.requiredTile.Clear(); recipe.requiredTile.Add(TileID.Furnaces);
            StorageSystem.Recipes.Build();
            check(before != BrowserContent.RecipeKey(recipe), "recipe revision invalidates a cached signature on the same recipe object");
            check(!BrowserCategories.Matches(ItemID.WorkBench, blocks, 1) && BrowserCategories.Matches(ItemID.Furnace, blocks, 1),
                "recipe revision refreshes workstation categories without a mod reload");

            nameValue.SetValue(originalName, "木头"); PinyinIndex.Prewarm(ItemID.Wood);
            bool initialPinyin = PinyinIndex.Matches(ItemID.Wood, "mutou");
            nameValue.SetValue(originalName, "石块");
            check(initialPinyin && PinyinIndex.Matches(ItemID.Wood, "shikuai") && !PinyinIndex.Matches(ItemID.Wood, "mutou"),
                "search reindexes the actual changed localized name instead of retaining old pinyin");

            Main.ItemDropsDB = new ItemDropDatabase();
            Main.ItemDropsDB.RegisterToItem(ItemID.Wood, rule);
            var acquisition = new AcquisitionRegistry();
            GameDataIndex.Build(acquisition, true);
            check(GameDataIndex.GetContainerDrops(ItemID.Wood).Any(drop => drop.ItemType == ItemID.StoneBlock),
                "registered container contents do not depend on English or Chinese name fragments");
            acquisition.RegisterSource(ItemKey.Crafted(ItemID.Wood), "manual source retained");
            Main.ItemDropsDB.RemoveFromItem(ItemID.Wood, rule);
            GameDataIndex.Build(acquisition, true);
            check(acquisition.GetTreasureBagContents(ItemID.Wood).Count == 0
                && acquisition.GetSources(ItemKey.Crafted(ItemID.Wood)).Contains("manual source retained")
                && !acquisition.GetSources(ItemKey.Crafted(ItemID.StoneBlock)).Any(source => source.StartsWith("开启「石块」")),
                "data rebuild removes withdrawn derived sources and preserves explicit registrations");

            var data = new Hashtable
            {
                ["good-first"] = new Hashtable { ["progression"] = 1f, ["npcIDs"] = new[] { NPCID.KingSlime } },
                ["bad"] = new Hashtable { ["progression"] = "not a rank" },
                ["nested-mod"] = new Hashtable { ["good-last"] = new Hashtable
                    { ["progression"] = 2d, ["npcIDs"] = new[] { NPCID.EyeofCthulhu }, ["spawnItems"] = new[] { ItemID.SuspiciousLookingEye } } }
            };
            var stages = new List<ProgressionTier>();
            bool parsed = new BossChecklistProvider().TryBuildFromData(data, stages, new AcquisitionRegistry());
            check(parsed && stages.Select(stage => stage.Id).SequenceEqual(new[] { "bc_good-first", "bc_good-last" })
                && stages[1].SpawnItems.Contains(ItemID.SuspiciousLookingEye),
                "BossChecklist accepts dictionary interfaces and isolates one malformed boss entry");

            var registry = new AcquisitionRegistry();
            var first = new AcquisitionRegistry(); var replacement = new AcquisitionRegistry();
            var key = ItemKey.Crafted(ItemID.Wood);
            registry.RegisterSource(key, "manual"); first.RegisterSource(key, "removed"); replacement.RegisterSource(key, "current");
            first.RegisterTreasureBag(ItemID.Wood, new int[] { ItemID.StoneBlock, ItemID.StoneBlock });
            registry.ReplaceDerived("fixture", first);
            check(registry.GetTreasureBagContents(ItemID.Wood).Count == 1, "container contents deduplicate repeated rule reports");
            registry.ReplaceDerived("fixture", replacement);
            check(registry.GetSources(key).SequenceEqual(new[] { "manual", "current" }) && registry.GetTreasureBagContents(ItemID.Wood).Count == 0,
                "replacing one derived layer retains user records and removes obsolete metadata");
            CalamityProfiles(check);
        }
        finally
        {
            Main.ItemDropsDB = dropDatabase;
            nameValue.SetValue(originalName, originalText); PinyinIndex.ClearCache();
            Main.recipe = recipes; Recipe.numRecipes = recipeCount; StorageSystem.Recipes.Build();
            BrowserContent.Reset(); BrowserCategories.Reset();
            GameDataIndex.Build(StorageSystem.Acquisition, true);
        }
    }

    private static bool OpenGate() => true;
    private static bool ClosedGate() => false;
    private static void CalamityProfiles(Action<bool, string> check)
    {
        var open = typeof(LifecycleAdaptationRegressionChecks).GetMethod(nameof(OpenGate), BindingFlags.Static | BindingFlags.NonPublic);
        var closed = typeof(LifecycleAdaptationRegressionChecks).GetMethod(nameof(ClosedGate), BindingFlags.Static | BindingFlags.NonPublic);
        bool read = AdaptiveMethodShape.TryHash(open, out string hash);
        var profile = new[] { new CalamitySourceCompatibility.MethodSpec("fixture", "gate", hash) };
        check(read && CalamitySourceCompatibility.Matches(profile, _ => new[] { open })
            && !CalamitySourceCompatibility.Matches(profile, _ => new[] { closed })
            && CalamitySourceCompatibility.Matches(profile, _ => new[] { open }),
            "one changed method body rejects only its route; an independent unchanged route remains established");
        if (Terraria.ModLoader.ModLoader.TryGetMod("CalamityMod", out var mod))
            foreach (string route in CalamitySourceCompatibility.Routes)
                check(CalamitySourceCompatibility.Supports(mod, route), "current installed Calamity route shape: " + route);
    }
}
