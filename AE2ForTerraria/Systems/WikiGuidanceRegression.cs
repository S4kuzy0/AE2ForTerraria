using System;
using System.Collections.Generic;
using System.Linq;
using AE2ForTerraria.Core;
using AE2ForTerraria.UI;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

internal static class WikiGuidanceRegression
{
    internal static void Run(Action<bool, string> check)
    {
        GameDataIndex.EnsureBuilt();
        var frog = WikiGuidance.Sources(ItemID.FrogLeg);
        check(frog.Any(r => r.Text.Contains("钓鱼")) && frog.Any(r => r.Text.Contains("Boss 前")),
            "wiki keeps pre-boss fishing alongside loaded drops and recipes");
        var squirrel = WikiGuidance.Sources(ItemID.Squirrel);
        check(squirrel.Any(r => r.Item == ItemID.BugNet && r.Link == GuideLink.Sources)
            && squirrel.Any(r => r.Npc == NPCID.Squirrel && r.Link == GuideLink.Npc),
            "capture guidance links the actual catchItem NPC and its required tool");
        check(WikiGuidance.Sources(int.MaxValue).Single().Text.Contains("未加载"),
            "unloaded wiki target produces an explicit unavailable page without an item lookup crash");
        check(WikiGuidance.Sources(ItemID.WorkBench).Any(r => r.Link == GuideLink.Recipe && r.Recipe.Length > 0),
            "source guide creates stable links to currently registered recipes");
        check(GameDataIndex.GetShops(ItemID.BugNet).Any(s => s.Npc == NPCID.Merchant),
            "shop guidance retains declared NPC identity without executing shop callbacks");

        var original = Main.recipe; int count = Recipe.numRecipes; bool menu = Main.gameMenu;
        try
        {
            int calls = 0;
            var unknown = new Condition("Mods.AE2ForTerraria.Tests.UnknownGuideCondition", () => ++calls > 0);
            Main.gameMenu = false;
            check(WikiGuidance.ConditionState(unknown) == null && calls == 0,
                "guidance leaves opaque condition unknown and does not execute side effects");
            Main.gameMenu = true;
            check(WikiGuidance.ConditionState(Condition.Hardmode) == null,
                "main-menu guidance does not claim current world condition state");

            void Recipes(params Recipe[] values) { Main.recipe = values; Recipe.numRecipes = values.Length; StorageSystem.Recipes.Build(); }
            BookmarkTree Build(int type, RecipeTreeStock pool)
            {
                var tree = new BookmarkTree(new BrowserBookmark(BrowserContent.Name(type)), pool);
                for (int tick = 0; tick < 1000 && !tree.Complete; tick++) tree.Advance(32);
                check(tree.Complete, "wiki next-step fixture finishes incrementally"); return tree;
            }
            var recipe = Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood, 2);
            Recipes(recipe);
            string signature = BrowserContent.RecipeKey(recipe);
            var stock = new Dictionary<int, long> { [ItemID.Wood] = 3 };
            var pool = new RecipeTreeStock(stock);
            var first = Build(ItemID.Torch, pool); var second = Build(ItemID.Torch, pool);
            check(GuideGoals.NextSteps(first).Any(r => r.Link == GuideLink.Recipe && r.Item == ItemID.Torch)
                && GuideGoals.NextSteps(second).Any(r => r.Link == GuideLink.Sources && r.Item == ItemID.Wood && r.Quantity == 1),
                "favorite next steps share inventory and distinguish crafting from collecting");
            check(stock[ItemID.Wood] == 3, "guidance never consumes the actual stock dictionary");
            check(WikiGuidance.RecipeHelp(ItemID.Torch, signature, 1).Any(r => r.Item == ItemID.Wood),
                "recipe guide reads current required ingredients");
            recipe.requiredItem[0] = new Item(ItemID.StoneBlock, 7); StorageSystem.Recipes.Build();
            check(WikiGuidance.RecipeHelp(ItemID.Torch, signature, 1).Any(r => r.Text.Contains("修改或移除")),
                "changed recipe invalidates the old signature instead of silently changing a followed route");
            var changedFavorite = new BookmarkTree(new BrowserBookmark(BrowserContent.Name(ItemID.Torch), signature), new RecipeTreeStock(stock));
            for (int tick = 0; tick < 1000 && !changedFavorite.Complete; tick++) changedFavorite.Advance(32);
            check(GuideGoals.NextSteps(changedFavorite).Any(r => r.Text.Contains("原收藏配方已变更")),
                "favorite guidance explains a replaced recipe instead of silently reusing an obsolete route");
            check(WikiGuidance.RecipeHelp(ItemID.Torch, "", 1).Any(r => r.Item == ItemID.StoneBlock)
                && !WikiGuidance.RecipeHelp(ItemID.Torch, "", 1).Any(r => r.Item == ItemID.Wood),
                "new route selection reflects changed ingredients immediately");
            Recipes();
            check(!WikiGuidance.Sources(ItemID.Torch).Any(r => r.Link == GuideLink.Recipe),
                "removed recipe disappears from source action links");
            var missing = new BookmarkTree(new BrowserBookmark("item:MissingWikiMod/FormerItem"));
            check(GuideGoals.NextSteps(missing).Single().Text.Contains("收藏仍保留"),
                "missing-mod goal retains its identity and explains why it cannot expand");
            Recipes(Recipe.Create(ItemID.StoneBlock).AddIngredient(ItemID.StoneWall, 4),
                Recipe.Create(ItemID.StoneWall, 4).AddIngredient(ItemID.StoneBlock));
            var cyclic = Build(ItemID.StoneBlock, new RecipeTreeStock(new Dictionary<int, long>()));
            check(GuideGoals.NextSteps(cyclic).Count() == 1 && GuideGoals.NextSteps(cyclic).Single().Link == GuideLink.Sources,
                "cyclic conversion becomes one collection action without a false ready-to-craft step");
        }
        finally { Main.recipe = original; Recipe.numRecipes = count; Main.gameMenu = menu; StorageSystem.Recipes.Build(); }
    }
}
