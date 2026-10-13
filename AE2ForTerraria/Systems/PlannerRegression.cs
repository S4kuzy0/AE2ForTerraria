using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

/// <summary>Opt-in controlled recipe tests; no world/player/file writes. Call Run(Check) from RegressionChecks.</summary>
public static class PlannerRegression
{
    public static void Run(Action<bool, string> check)
    {
        Recipe[] savedRecipes = Main.recipe;
        int savedCount = Recipe.numRecipes;
        const int testGroup = int.MaxValue - 71;
        bool hadGroup = RecipeGroup.recipeGroups.TryGetValue(testGroup, out var savedGroup);
        const int goal = ItemID.Torch, partA = ItemID.WoodenSword, partB = ItemID.WoodenBow;
        const int rawX = ItemID.Wood, rawY = ItemID.StoneBlock, rawZ = ItemID.Gel, rawW = ItemID.DirtBlock;
        try
        {
            CraftPlanner SetRecipes(params Recipe[] recipes)
            {
                Main.recipe = recipes; Recipe.numRecipes = recipes.Length;
                var index = new RecipeIndex(); index.Build(); return new CraftPlanner(index);
            }
            StorageLedger Stock(params (int Type, long Amount)[] values)
            {
                var ledger = new StorageLedger(1, 1, int.MaxValue);
                foreach (var value in values) ledger.Restore(ItemKey.Crafted(value.Type), value.Amount);
                return ledger;
            }
            long Count(Dictionary<ItemKey, long> values, int type) => values.GetValueOrDefault(ItemKey.Crafted(type));
            for (int order = 0; order < 4; order++)
            {
                Recipe root = Recipe.Create(goal);
                if ((order & 1) == 0) root.AddIngredient(partA).AddIngredient(partB);
                else root.AddIngredient(partB).AddIngredient(partA);
                Recipe first = Recipe.Create(partA).AddIngredient((order & 2) == 0 ? rawX : rawY);
                Recipe second = Recipe.Create(partA).AddIngredient((order & 2) == 0 ? rawY : rawX);
                var planner = SetRecipes(root, first, second, Recipe.Create(partB).AddIngredient(rawX));
                var stock = Stock((rawX, 1), (rawY, 1));
                var plan = planner.Plan(stock, ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(plan.FullySatisfied && Count(plan.Consumed, rawX) == 1 && Count(plan.Consumed, rawY) == 1
                    && Count(plan.Outputs, goal) == 1, "competing prerequisite routes, ordering " + order);
                check(stock.GetAmount(ItemKey.Crafted(rawX)) == 1 && stock.GetAmount(ItemKey.Crafted(rawY)) == 1,
                    "planning leaves real stock unchanged, ordering " + order);
            }

            RecipeGroup.recipeGroups[testGroup] = new RecipeGroup(() => "AE2 controlled alternatives", rawX, rawY);
            for (int order = 0; order < 2; order++)
            {
                Recipe root = Recipe.Create(goal); root.acceptedGroups.Add(testGroup);
                if (order == 0) root.AddIngredient(rawX).AddIngredient(partB);
                else root.AddIngredient(partB).AddIngredient(rawX);
                var planner = SetRecipes(root, Recipe.Create(partB).AddIngredient(rawY));
                var plan = planner.Plan(Stock((rawX, 1), (rawY, 1)), ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(plan.FullySatisfied && Count(plan.Consumed, rawX) == 1 && Count(plan.Consumed, rawY) == 1,
                    "group stock preserves strict downstream ingredient, ordering " + order);
            }
            {
                Recipe root = Recipe.Create(goal).AddIngredient(rawX, 3); root.acceptedGroups.Add(testGroup);
                var planner = SetRecipes(root);
                var plan = planner.Plan(Stock((rawX, 1), (rawY, 2)), ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(plan.FullySatisfied && Count(plan.Consumed, rawX) == 1 && Count(plan.Consumed, rawY) == 2,
                    "one group consumes mixed existing alternatives");
            }
            {
                Recipe root = Recipe.Create(goal).AddIngredient(rawX, 2); root.acceptedGroups.Add(testGroup);
                var planner = SetRecipes(root, Recipe.Create(rawX).AddIngredient(rawZ), Recipe.Create(rawY).AddIngredient(rawW));
                var plan = planner.Plan(Stock((rawZ, 1), (rawW, 1)), ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(plan.FullySatisfied && Count(plan.Consumed, rawZ) == 1 && Count(plan.Consumed, rawW) == 1,
                    "one group mixes manufactured alternatives");
            }
            {
                var planner = SetRecipes(Recipe.Create(goal).AddIngredient(rawX), Recipe.Create(goal).AddIngredient(rawY));
                var plan = planner.Plan(Stock((rawX, 1), (rawY, 1)), ItemKey.Crafted(goal), 2, useExistingTarget:false);
                check(plan.FullySatisfied && Count(plan.Outputs, goal) == 2
                    && Count(plan.Consumed, rawX) == 1 && Count(plan.Consumed, rawY) == 1,
                    "one requested quantity mixes production routes");
            }
            {
                var planner = SetRecipes(Recipe.Create(goal, 3).AddIngredient(partA).AddIngredient(rawY),
                    Recipe.Create(partA, 4).AddIngredient(rawX));
                var plan = planner.Plan(Stock((rawX, 1), (rawY, 1), (goal, 20)), ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(plan.FullySatisfied && Count(plan.Outputs, goal) == 3 && Count(plan.Outputs, partA) == 3
                    && !plan.Consumed.ContainsKey(ItemKey.Crafted(goal)), "batch surplus and existing target preserved");
                var existing = planner.Plan(Stock((goal, 20)), ItemKey.Crafted(goal), 1);
                check(existing.FullySatisfied && existing.ProducedAmount == 0, "explicit default existing-target semantics preserved");
            }
            {
                var planner = SetRecipes(Recipe.Create(goal).AddIngredient(partA).AddIngredient(partB),
                    Recipe.Create(partA, 2).AddIngredient(rawX), Recipe.Create(partA).AddIngredient(rawY),
                    Recipe.Create(partB).AddIngredient(rawX));
                var stock = Stock((rawY, 1));
                ItemKey variant = new ItemKey(rawX, 7);
                stock.Restore(variant, 1);
                var plan = planner.Plan(stock, ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(plan.FullySatisfied && plan.Consumed.GetValueOrDefault(variant) == 1
                    && Count(plan.Consumed, rawY) == 1 && Count(plan.Outputs, partA) == 0
                    && Count(plan.Outputs, goal) == 1,
                    "rollback removes rejected-route surplus and preserves variant identity");
            }
            {
                var planner = SetRecipes(Recipe.Create(goal, 3).AddIngredient(rawX, 2), Recipe.Create(goal).AddIngredient(rawY));
                var plan = planner.Plan(Stock((rawX, 2), (rawY, 2)), ItemKey.Crafted(goal), 5, useExistingTarget:false);
                check(plan.FullySatisfied && Count(plan.Outputs, goal) == 5
                    && Count(plan.Consumed, rawX) == 2 && Count(plan.Consumed, rawY) == 2,
                    "mixed production routes retain exact nonuniform batch arithmetic");
            }
            {
                var planner = SetRecipes(Recipe.Create(goal).AddIngredient(partA), Recipe.Create(partA).AddIngredient(goal));
                var plan = planner.Plan(Stock(), ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(!plan.FullySatisfied && !plan.Limited && plan.Missing.Count > 0, "cycle terminates without inventing stock");
            }
            {
                var planner = SetRecipes(Recipe.Create(goal).AddIngredient(rawX, 2).AddIngredient(rawY, 3));
                var plan = planner.Plan(Stock(), ItemKey.Crafted(goal), 1, useExistingTarget:false);
                check(!plan.FullySatisfied && !plan.Limited && plan.Missing.Count == 2
                    && plan.Missing.Exists(x => x.Key.Type == rawX && x.Amount == 2)
                    && plan.Missing.Exists(x => x.Key.Type == rawY && x.Amount == 3), "leaf missing diagnostics preserved");
                var denied = planner.Plan(Stock((rawX, 2), (rawY, 3)), ItemKey.Crafted(goal), 1, _ => false, false);
                check(!denied.FullySatisfied, "candidate search cannot bypass recipe filter");
            }
            {
                var recipes = new List<Recipe>();
                for (int i = 0; i < 18; i++)
                {
                    recipes.Add(Recipe.Create(100 + i).AddIngredient(101 + i));
                    recipes.Add(Recipe.Create(100 + i).AddIngredient(101 + i));
                }
                var planner = SetRecipes(recipes.ToArray());
                var plan = planner.Plan(Stock(), ItemKey.Crafted(100), 1, useExistingTarget:false);
                check(!plan.FullySatisfied && plan.Limited, "finite budget reports unresolved large branching search");
            }
        }
        finally
        {
            Main.recipe = savedRecipes; Recipe.numRecipes = savedCount;
            if (hadGroup) RecipeGroup.recipeGroups[testGroup] = savedGroup;
            else RecipeGroup.recipeGroups.Remove(testGroup);
        }
    }
}
