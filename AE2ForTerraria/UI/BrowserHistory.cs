using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AE2ForTerraria.Core;
using Terraria;

namespace AE2ForTerraria.UI;

internal sealed record BrowserVisit(string Content, int Prefix, string ItemData, int Tab,
    string Recipe, int Scroll, int Stations, long Quantity)
{
    internal bool TryResolve(out ItemKey key, out int npc)
    {
        key = ItemKey.None; npc = 0;
        if (!BrowserContent.Resolve(Content, out int type, out bool isNpc)) return false;
        if (isNpc) npc = type;
        else
        {
            try
            {
                if (Prefix < 0) return false;
                key = new ItemKey(type, Prefix, data: ItemData);
                Item item = key.CreateItem();
                if (item.type != type || item.ModItem is Terraria.ModLoader.Default.UnloadedItem
                    || ItemData == null && item.prefix != Prefix) return false;
                key = ItemKey.FromItem(item);
            }
            catch { key = ItemKey.None; return false; }
        }
        return true;
    }
    internal string Label
    {
        get
        {
            if (!BrowserContent.Resolve(Content, out int type, out bool npc)) return "未加载：" + Content;
            return (npc ? Systems.NpcIndex.GetName(type) : Lang.GetItemNameValue(type)) + " · "
                + (npc ? "怪物 / NPC" : new[] { "配方", "来源", "用途", "合成树" }[Math.Clamp(Tab, 0, 3)]);
        }
    }
}

/// <summary>Recent visits persist independently of the current directory-to-detail navigation stack.</summary>
internal static class BrowserHistory
{
    private const string FileName = "AE2-BrowserHistory.json";
    internal const int Capacity = 10;
    private static List<BrowserVisit> _entries;
    private static bool _canSave;
    internal static IReadOnlyList<BrowserVisit> Entries { get { Load(); return _entries; } }
    private static void Load()
    {
        if (_entries != null) return;
        _entries = new(); _canSave = true;
        try
        {
            using var document = BrowserPreferences.Read(FileName);
            if (document != null)
                _entries = (document.RootElement.Deserialize<List<BrowserVisit>>() ?? new())
                    .Where(p => p != null && !string.IsNullOrEmpty(p.Content) && p.Content.Length < 512
                        && (p.ItemData?.Length ?? 0) <= 256 * 1024 && (p.Recipe?.Length ?? 0) <= 128 && p.Quantity > 0 && p.Tab >= 0 && p.Tab <= 3)
                    .Take(Capacity).ToList();
        }
        catch (Exception ex)
        {
            _canSave = false;
            AE2ForTerraria.Instance?.Logger.Warn("Cannot read browser history; original file preserved: " + ex.Message);
        }
    }
    internal static void Record(BrowserVisit visit)
    {
        if (visit == null || string.IsNullOrEmpty(visit.Content) || (visit.ItemData?.Length ?? 0) > 256 * 1024) return;
        Load();
        if (_entries.Count > 0)
        {
            var latest = _entries[0];
            if (latest.Content == visit.Content && latest.Tab == visit.Tab && latest.Recipe == visit.Recipe)
            {
                if (latest == visit) return;
                _entries[0] = visit; Save(); return;
            }
        }
        _entries.Insert(0, visit);
        if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        Save();
    }
    private static void Save() { if (_canSave) BrowserPreferences.Write(FileName, _entries); }
    internal static void Reset() { _entries = null; _canSave = true; }
}
