using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using AE2ForTerraria.Core;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.UI;

internal static class BrowserContent
{
    private static ConditionalWeakTable<Recipe, Signature> _signatures = new();
    private static RecipeIndex _recipeIndex;
    private static int _recipeRevision = -1;
    private static readonly System.Collections.Generic.Dictionary<(int Type, bool Npc), string> Names = new();
    private sealed record Signature(string Value);
    internal static string Name(int type, bool npc = false)
    {
        if (Names.TryGetValue((type, npc), out string name)) return name;
        name = npc ? "npc:" + (type < NPCID.Count ? "Terraria/" + type : NPCLoader.GetNPC(type)?.FullName)
            : "item:" + (type < ItemID.Count ? "Terraria/" + type : ItemLoader.GetItem(type)?.FullName);
        Names[(type, npc)] = name; return name;
    }
    internal static bool Resolve(string name, out int type, out bool npc)
    {
        type = 0; npc = name?.StartsWith("npc:", StringComparison.Ordinal) == true;
        if (name == null || !npc && !name.StartsWith("item:", StringComparison.Ordinal)) return false;
        string content = name[(npc ? 4 : 5)..];
        int separator = content.IndexOf('/');
        if (separator <= 0 || separator == content.Length - 1) return false;
        if (content.StartsWith("Terraria/", StringComparison.Ordinal))
        {
            if (!int.TryParse(content[9..], out type)) return false;
            return type > 0 && type < (npc ? NPCID.Count : ItemID.Count);
        }
        if (npc && ModContent.TryFind<ModNPC>(content, out var modNpc)) type = modNpc.Type;
        else if (!npc && ModContent.TryFind<ModItem>(content, out var item)) type = item.Type;
        return type > 0;
    }
    internal static string RecipeKey(Recipe recipe)
    {
        if (recipe == null) return "";
        var index = Systems.StorageSystem.Recipes;
        if (!ReferenceEquals(_recipeIndex, index) || _recipeRevision != (index?.Revision ?? -1))
        {
            _signatures = new(); _recipeIndex = index; _recipeRevision = index?.Revision ?? -1;
        }
        return _signatures.GetValue(recipe, r =>
        {
            string TileName(int tile) => tile < TileID.Count ? "Terraria/" + tile : TileLoader.GetTile(tile)?.FullName ?? "missing";
            var text = new StringBuilder(Name(r.createItem.type)).Append(':').Append(r.createItem.stack);
            foreach (var item in r.requiredItem.Where(i => !i.IsAir)) text.Append('|').Append(Name(item.type)).Append(':').Append(item.stack);
            foreach (int tile in r.requiredTile.Where(t => t >= 0)) text.Append('|').Append(TileName(tile));
            foreach (int id in r.acceptedGroups)
                if (RecipeGroup.recipeGroups.TryGetValue(id, out var group))
                    text.Append("|group:").AppendJoin(',', group.ValidItems.Select(t => Name(t)).OrderBy(n => n, StringComparer.Ordinal));
                else text.Append("|missing-group");
            foreach (var condition in r.Conditions) text.Append('|').Append(condition?.Description?.Key ?? "missing-condition");
            return new Signature(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
        }).Value;
    }
    internal static int ResolveRecipe(int type, string signature)
    {
        var recipes = Systems.StorageSystem.Recipes?.GetRecipesFor(ItemKey.Crafted(type));
        if (recipes == null || recipes.Count == 0) return -1;
        if (!string.IsNullOrEmpty(signature))
            foreach (int index in recipes) if (RecipeKey(Main.recipe[index]) == signature) return index;
        return recipes[0];
    }
    internal static void Reset() { _signatures = new(); Names.Clear(); _recipeIndex = null; _recipeRevision = -1; }
}
