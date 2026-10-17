using AE2ForTerraria.Core;
using Terraria;

namespace AE2ForTerraria.UI;

/// <summary>Let Terraria and the active mods compose their normal item tooltip, preserving custom data.</summary>
internal static class BrowserHover
{
    private static ItemKey _key;
    private static Item _item;
    private static ulong _at;
    internal static bool IsCurrent(Item item) => _item != null && item.type == _key.Type && _at == Main.GameUpdateCount;
    public static void Item(ItemKey key)
    {
        if (key.IsAir) return;
        if (_item == null || !_key.Equals(key)) { _key = key; _item = key.CreateItem(); }
        Main.HoverItem = _item;
        Main.hoverItemName = _item.Name;
        _at = Main.GameUpdateCount;
        if (ArmorSetIndex.IsArmor(_item)) ArmorSetIndex.Request(_item.type, out _);
    }
    public static void Reset() { _key = ItemKey.None; _item = null; }
}
