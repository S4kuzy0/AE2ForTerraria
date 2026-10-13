// Harness-only substitutes. Planning/index/identity/ledger implementations are linked from real source.
// No real Terraria initialization, hooks, persistence, UI, network, world, or player are exercised here.
using System;
using System.Collections.Generic;
namespace Terraria
{
    public sealed class Item
    {
        public int type, stack, prefix;
        public string Data;
        public bool IsAir => type <= 0 || stack <= 0;
        public Item(int type = 0, int stack = 1) { this.type = type; this.stack = stack; }
        public void TurnToAir() { type = 0; stack = 0; prefix = 0; Data = null; }
    }
    public sealed class Recipe
    {
        public static int numRecipes;
        public bool Disabled;
        public Item createItem;
        public List<Item> requiredItem = new();
        public List<int> acceptedGroups = new();
        public static Recipe Create(int type, int stack = 1) => new() { createItem = new Item(type, stack) };
        public Recipe AddIngredient(int type, int stack = 1) { requiredItem.Add(new Item(type, stack)); return this; }
    }
    public static class Main { public static Recipe[] recipe = Array.Empty<Recipe>(); }
    public sealed class RecipeGroup
    {
        public static Dictionary<int, RecipeGroup> recipeGroups = new();
        public HashSet<int> ValidItems;
        public RecipeGroup(Func<string> name, params int[] types) { ValidItems = new(types); }
    }
}
namespace Terraria.ID
{
    public static class ItemID
    {
        public const int Torch = 8, Wood = 9, StoneBlock = 3, Gel = 23, DirtBlock = 2;
        public const int WoodenSword = 24, WoodenBow = 39;
    }
}
namespace AE2ForTerraria.Core
{
    public static class ItemPersistence
    {
        public static ItemKey KeyOf(Terraria.Item item) => new(item.type, item.prefix, data:item.Data);
        public static Terraria.Item Create(ItemKey key, int stack) => new(key.Type, stack) { prefix = key.Prefix, Data = key.Data };
    }
    public static class RecipeExecutionPolicy { public static string BlockReason(Terraria.Recipe recipe) => null; }
}
