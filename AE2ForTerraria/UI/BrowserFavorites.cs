using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Terraria;

namespace AE2ForTerraria.UI;

internal sealed record BrowserBookmark(string Content, string Recipe = "", long Quantity = 1);

/// <summary>Stable personal bookmarks. Legacy item/NPC favorites are retained on upgrade.</summary>
internal static class BrowserFavorites
{
    private sealed record Saved(int Version, bool AlwaysVisible, List<BrowserBookmark> Entries);
    private const string FileName = "AE2-BrowserFavorites.json";
    private static HashSet<string> _keys;
    private static List<BrowserBookmark> _entries;
    private static bool _alwaysVisible, _canSave;
    private static int _revision;
    internal static IReadOnlyList<BrowserBookmark> Entries { get { Load(); return _entries; } }
    internal static int Revision { get { Load(); return _revision; } }
    internal static bool AlwaysVisible
    {
        get { Load(); return _alwaysVisible; }
        set { Load(); if (_alwaysVisible == value) return; _alwaysVisible = value; Save(); }
    }
    private static void Load()
    {
        if (_keys != null) return;
        _keys = new(StringComparer.Ordinal); _entries = new(); _alwaysVisible = true; _canSave = true;
        try
        {
            using var document = BrowserPreferences.Read(FileName);
            if (document == null) return;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (string key in document.RootElement.Deserialize<List<string>>() ?? new()) Add(new BrowserBookmark(key));
            }
            else
            {
                var saved = document.RootElement.Deserialize<Saved>();
                if (saved?.Version != 2) throw new InvalidOperationException("Unsupported browser favorite format.");
                _alwaysVisible = saved.AlwaysVisible;
                foreach (var entry in saved.Entries ?? new()) Add(entry);
            }
        }
        catch (Exception ex)
        {
            _canSave = false;
            AE2ForTerraria.Instance?.Logger.Warn("Cannot read browser favorites; original file preserved: " + ex.Message);
        }
    }
    private static void Add(BrowserBookmark entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Content) || entry.Content.Length > 512
            || entry.Quantity < 1 || (entry.Recipe?.Length ?? 0) > 128 || !_keys.Add(entry.Content)) return;
        _entries.Add(entry);
    }
    public static bool Contains(int type, bool npc = false) { Load(); return _keys.Contains(BrowserContent.Name(type, npc)); }
    public static bool Toggle(int type, bool npc = false, int recipeIndex = -1, long quantity = 1)
    {
        if (type <= 0) return false;
        Load(); string key = BrowserContent.Name(type, npc);
        if (_keys.Contains(key)) return Remove(key);
        if (recipeIndex >= 0 && recipeIndex < Recipe.numRecipes && Main.recipe[recipeIndex].createItem.type != type) recipeIndex = -1;
        if (!npc && recipeIndex < 0) recipeIndex = BrowserContent.ResolveRecipe(type, "");
        string signature = recipeIndex >= 0 && recipeIndex < Recipe.numRecipes ? BrowserContent.RecipeKey(Main.recipe[recipeIndex]) : "";
        Add(new BrowserBookmark(key, signature, Math.Max(1, quantity)));
        _revision++; return Save();
    }
    internal static bool Remove(string content)
    {
        Load(); if (!_keys.Remove(content)) return false;
        _entries.RemoveAll(entry => entry.Content == content); _revision++; return Save();
    }
    private static bool Save()
    {
        if (_canSave) return BrowserPreferences.Write(FileName, new Saved(2, _alwaysVisible, _entries));
        Systems.UIStatus.Toast("收藏仅保留本次会话：原设置文件有误，已保留原文件"); return false;
    }
    public static void Reset() { _keys = null; _entries = null; _revision++; }
}
