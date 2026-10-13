using System;
using AE2ForTerraria.Systems;
using AE2ForTerraria.UI;

namespace AE2ForTerraria.Core;

/// <summary>Shared inventory search: all terms must match; @mod limits source, !term excludes a term.</summary>
public static class ItemSearch
{
    public static bool Matches(ItemKey key, string query)
    {
        foreach (string raw in (query ?? "").Split(' ',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool exclude=raw.StartsWith('!'); string term=exclude ? raw[1..] : raw;
            if (term.Length==0) continue;
            bool matches=term.StartsWith('@')
                ? (UIItemNames.ModNameOf(key.Type) ?? "Terraria").Contains(term[1..],StringComparison.OrdinalIgnoreCase)
                : PinyinIndex.Matches(key.Type,term) || UIItemNames.Display(key).Contains(term,StringComparison.OrdinalIgnoreCase);
            if (matches==exclude) return false;
        }
        return true;
    }
}
