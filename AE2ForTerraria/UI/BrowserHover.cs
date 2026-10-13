using AE2ForTerraria.Core;
using Terraria;

namespace AE2ForTerraria.UI;

/// <summary>Let Terraria and the active mods compose their normal item tooltip, preserving custom data.</summary>
internal static class BrowserHover
{
    private static ItemKey _key;
    private static Item _item;
    public static void Item(ItemKey key)
    {
        if (key.IsAir) return;
        if (_item == null || !_key.Equals(key)) { _key = key; _item = key.CreateItem(); }
        Main.HoverItem = _item;
        Main.hoverItemName = _item.Name;
    }
    public static void Reset() { _key = ItemKey.None; _item = null; }
}
