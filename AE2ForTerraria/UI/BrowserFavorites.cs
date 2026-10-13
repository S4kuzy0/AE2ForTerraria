using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using Terraria.ModLoader;

namespace AE2ForTerraria.UI;

/// <summary>Personal presentation preference. Stable names survive content reordering and disabled mods.</summary>
internal static class BrowserFavorites
{
    private static HashSet<string> _keys;
    private static string PathName => Path.Combine(Main.SavePath, "AE2-BrowserFavorites.json");
    private static string Key(int type, bool npc) => npc
        ? "npc:" + (type < Terraria.ID.NPCID.Count ? "Terraria/" + type : NPCLoader.GetNPC(type)?.FullName)
        : "item:" + (type < Terraria.ID.ItemID.Count ? "Terraria/" + type : ItemLoader.GetItem(type)?.FullName);
    private static void Load()
    {
        if (_keys != null) return;
        _keys = new();
        try { if (File.Exists(PathName)) _keys = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(PathName)) ?? new(); }
        catch (Exception ex) { AE2ForTerraria.Instance?.Logger.Warn("Cannot read browser favorites: " + ex.Message); }
    }
    public static bool Contains(int type, bool npc = false) { Load(); return _keys.Contains(Key(type, npc)); }
    public static void Toggle(int type, bool npc = false)
    {
        Load(); string key = Key(type, npc); if (!_keys.Remove(key)) _keys.Add(key);
        try { File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(_keys)); File.Move(PathName + ".tmp", PathName, true); }
        catch (Exception ex) { AE2ForTerraria.Instance?.Logger.Warn("Cannot save browser favorites: " + ex.Message); Systems.UIStatus.Toast("收藏保存失败，请查看日志"); }
    }
    public static void Reset() { _keys = null; }
}
