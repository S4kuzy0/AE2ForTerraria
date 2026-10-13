using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using AE2ForTerraria.Content.Tiles;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.Systems;

/// <summary>Catalogue truth and cache invalidation; temporary recipes only, no player or world writes.</summary>
public static class CraftabilityRegression
{
    public static void Run(Action<bool, string> check)
    {
        var original = Main.recipe; int count = Recipe.numRecipes;
        var target = ItemKey.Crafted(ItemID.Torch);
        var intermediate = ItemKey.Crafted(ItemID.WoodenSword);
        var wood = ItemKey.Crafted(ItemID.Wood);
        var stone = ItemKey.Crafted(ItemID.StoneBlock);
        try
        {
            var gate = Recipe.Create(ItemID.Torch, 3).AddIngredient(ItemID.WoodenSword);
            var source = Recipe.Create(ItemID.WoodenSword).AddIngredient(ItemID.Wood, 2);
            Main.recipe = new[] { gate, source }; Recipe.numRecipes = 2;
            var index = new RecipeIndex(); index.Build(); var planner = new CraftPlanner(index);
            var cache = new CraftabilityCache(); var context = new object();
            bool available = true;
            bool Allowed(Recipe recipe) => available;
            void Use(Dictionary<ItemKey, long> stock) => cache.SetContext(planner, stock, context, Allowed);
            void Complete() => cache.Advance(100, 1000);

            Use(new() { [target] = 99 });
            check(!cache.IsCraftable(target.Type), "unchecked catalogue entries are never craftable");
            Complete();
            check(!cache.IsCraftable(target.Type), "existing target stock cannot replace new production requirements");
            Use(new() { [wood] = 1 }); Complete();
            check(!cache.IsCraftable(target.Type), "deepest missing material excludes a recipe from craftable");
            Use(new() { [wood] = 2 }); Complete();
            check(cache.IsCraftable(target.Type), "complete recursive material tree is craftable");
            int computed = cache.PlansComputed;
            Use(new() { [wood] = 2 }); Complete();
            check(cache.IsCraftable(target.Type) && cache.PlansComputed == computed,
                "equal aggregate dictionaries retain cached plans without recomputation");
            Use(new() { [wood] = 2, [ItemKey.Crafted(ItemID.CopperCoin)] = 100 }); Complete();
            check(cache.IsCraftable(target.Type) && cache.PlansComputed == computed,
                "unrelated stock changes retain a validated crafting tree without recomputation");
            Use(new() { [wood] = 1 });
            check(!cache.IsCraftable(target.Type), "real stock change immediately clears an old positive result");
            Complete();
            Use(new() { [intermediate] = 1 }); Complete();
            check(cache.IsCraftable(target.Type), "stored intermediate satisfies its subtree without upstream materials");
            available = false;
            check(!cache.IsCraftable(target.Type), "changed recipe condition immediately rejects a cached positive");
            Complete();
            available = true; Complete();
            check(cache.IsCraftable(target.Type), "condition restoration invalidates a negative plan and discovers availability");

            var alternate = Recipe.Create(ItemID.Torch, 3).AddIngredient(ItemID.StoneBlock);
            Main.recipe = new[] { gate, source, alternate }; Recipe.numRecipes = 3;
            index = new RecipeIndex(); index.Build(); planner = new CraftPlanner(index);
            Use(new() { [stone] = 1 }); Complete();
            check(cache.IsCraftable(target.Type), "one complete alternative recipe is sufficient even when another route lacks materials");
            var batch = planner.Plan(new Dictionary<ItemKey, long> { [stone] = 1 }, target, 4, useExistingTarget: false);
            check(!batch.FullySatisfied, "selected quantity recomputes full recipe batches instead of reusing one-item availability");
            context = new object(); Use(new() { [stone] = 1 });
            check(!cache.IsCraftable(target.Type), "changing network context never inherits another terminal's positive cache");

            var policy = new TerminalPolicyState(); var entrance = new Point16(30, 30);
            policy.Apply(entrance, 3, new Point16(28, 30), new int[] { ItemID.Wood });
            check(policy.Matches(entrance, 3) && policy.IsLocked(ItemID.Wood) && !policy.IsLocked(ItemID.StoneBlock),
                "terminal lock policy uses the authoritative session instead of local topology");
            check(!policy.Matches(entrance, 4) && !policy.Matches(new Point16(31, 30), 3),
                "terminal lock policy cannot leak across sessions or entrance changes");
            policy.Reset(); check(!policy.Matches(entrance, 3), "terminal close clears received material policy");

            Main.recipe = new[] { Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood),
                Recipe.Create(ItemID.Torch).AddIngredient(ItemID.StoneBlock) };
            Recipe.numRecipes = Main.recipe.Length; StorageSystem.Recipes.Build();
            using var world = new RegressionWorld();
            var terminal = world.Place(new MECraftingTerminalEntity(), ModContent.TileType<MECraftingTerminalTile>(), 30, 30);
            var storage = world.Place(new MEStorageUnitEntity(), ModContent.TileType<MEStorageUnitTile>(), 31, 30);
            storage.Ledger.MaxBytes = long.MaxValue; storage.Ledger.MaxTypes = long.MaxValue;
            storage.Ledger.Restore(wood, 1); storage.Ledger.Restore(stone, 1);
            var anchor = MENetwork.CanonicalAnchor(MENetwork.Collect(30, 30));
            NetworkRegistry.TryClaim(anchor, "owner"); NetworkRegistry.SetLevel(anchor, "guest", NetworkRegistry.LevelUse);
            NetworkRegistry.SetLocked(anchor, ItemID.Wood, true);
            terminal.Enqueue(target, 1, "guest"); terminal.Update();
            check(terminal.Jobs.Count == 0 && storage.Ledger.GetAmount(target) == 1
                && storage.Ledger.GetAmount(wood) == 1 && storage.Ledger.GetAmount(stone) == 0,
                "executor chooses an unlocked alternate route and leaves protected material untouched");
        }
        finally { Main.recipe = original; Recipe.numRecipes = count; StorageSystem.Recipes?.Build(); }
    }
}
