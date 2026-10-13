using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;

namespace AE2ForTerraria.Systems;

/// <summary>Normal quantity limits must return Limited without throwing first-chance overflow exceptions.</summary>
public static class PlannerQuantityRegression
{
    public static void Run(Action<bool, string> check)
    {
        Recipe[] original = Main.recipe; int originalCount = Recipe.numRecipes;
        ItemKey wood = ItemKey.Crafted(ItemID.Wood), stone = ItemKey.Crafted(ItemID.StoneBlock), target = ItemKey.Crafted(ItemID.Torch);
        var stock = new Dictionary<ItemKey, long> { [wood] = long.MaxValue, [stone] = long.MaxValue };
        int overflowCount = 0, thread = Environment.CurrentManagedThreadId;
        void FirstChance(object sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId == thread && args.Exception is OverflowException) overflowCount++;
        }
        CraftPlanner Recipes(params Recipe[] recipes)
        {
            Main.recipe = recipes; Recipe.numRecipes = recipes.Length;
            var index = new RecipeIndex(); index.Build(); return new CraftPlanner(index);
        }
        AppDomain.CurrentDomain.FirstChanceException += FirstChance;
        try
        {
            var planner = Recipes(Recipe.Create(ItemID.Torch, 3).AddIngredient(ItemID.Wood));
            bool rejected = true;
            for (int retry = 0; retry < 4; retry++)
            {
                CraftPlan plan = planner.Plan(stock, target, long.MaxValue, useExistingTarget: false);
                rejected &= plan.Limited && !plan.FullySatisfied;
            }
            check(rejected && overflowCount == 0,
                "repeated rounded-batch overflow returns Limited without first-chance exceptions or an executable plan");
            CraftPlan bounded = planner.Plan(stock, target, long.MaxValue - 1, useExistingTarget: false);
            check(bounded.FullySatisfied && bounded.Outputs[target] == long.MaxValue - 1
                && bounded.Consumed[wood] == (long.MaxValue - 1) / 3,
                "largest representable triple-output batch keeps exact produced and consumed quantities");
            CraftPlan fallback = planner.Plan(stock, target, 1, useExistingTarget: false);
            check(fallback.FullySatisfied && fallback.Outputs[target] == 3 && fallback.Consumed[wood] == 1,
                "a normal single-batch fallback remains usable after the rejected full order");

            planner = Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood, 3));
            CraftPlan ingredientLimit = planner.Plan(stock, target, long.MaxValue, useExistingTarget: false);
            check(ingredientLimit.Limited && !ingredientLimit.FullySatisfied && overflowCount == 0,
                "ingredient multiplication beyond Int64 returns Limited without throwing");

            planner = Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.WoodenSword).AddIngredient(ItemID.WoodenBow),
                Recipe.Create(ItemID.WoodenSword).AddIngredient(ItemID.Wood),
                Recipe.Create(ItemID.WoodenBow).AddIngredient(ItemID.Wood));
            CraftPlan missingLimit = planner.Plan(new Dictionary<ItemKey, long>(), target, long.MaxValue, useExistingTarget: false);
            check(missingLimit.Limited && !missingLimit.FullySatisfied && overflowCount == 0,
                "shared missing-material additions beyond Int64 remain limited diagnostics without throwing");

            planner = Recipes(Recipe.Create(ItemID.Torch).AddIngredient(ItemID.Wood));
            CraftPlan exact = planner.Plan(stock, target, long.MaxValue, useExistingTarget: false);
            check(exact.FullySatisfied && exact.Outputs[target] == long.MaxValue && exact.Consumed[wood] == long.MaxValue
                && stock[wood] == long.MaxValue && stock[stone] == long.MaxValue && overflowCount == 0,
                "representable maximum order remains exact and every boundary probe leaves authoritative stock unchanged");

            var expected = new InvalidOperationException("AE2 controlled recipe callback failure");
            bool propagated = false;
            try { planner.Plan(stock, target, 1, _ => throw expected, useExistingTarget: false); }
            catch (InvalidOperationException actual) { propagated = ReferenceEquals(expected, actual); }
            check(propagated, "unrelated recipe callback failures propagate instead of becoming numeric-limit diagnostics");
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= FirstChance;
            Main.recipe = original; Recipe.numRecipes = originalCount;
        }
    }
}
