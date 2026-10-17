using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace AE2ForTerraria.UI;

internal static class ArmorSetIndex
{
    private sealed class Query(IEnumerator<(Item Head, Item Body, Item Legs)> candidates)
    {
        internal readonly IEnumerator<(Item Head, Item Body, Item Legs)> Candidates = candidates;
        internal bool Complete;
    }
    private static readonly Dictionary<int, Query> Queries = new();
    private static readonly Dictionary<int, HashSet<string>> Descriptions = new();
    private static readonly HashSet<Type> Failed = new();
    private static Item[] _heads, _bodies, _legs;
    private static int _priority;
    private static string _culture;
    internal static bool IsArmor(Item item) => item != null && !item.IsAir && !item.vanity && (item.headSlot >= 0 || item.bodySlot >= 0 || item.legSlot >= 0);
    internal static IReadOnlyCollection<string> Request(int type, out bool pending)
    {
        pending = false;
        string culture = Terraria.Localization.Language.ActiveCulture.Name;
        if (_culture != culture) { Reset(); _culture = culture; }
        if (!ContentSamples.ItemsByType.TryGetValue(type, out var item) || !IsArmor(item)) return Array.Empty<string>();
        _priority = type;
        if (!Queries.ContainsKey(type)) Queries[type] = new Query(Candidates(item).GetEnumerator());
        pending = !Queries[type].Complete;
        return Descriptions.TryGetValue(type, out var texts) ? texts : Array.Empty<string>();
    }
    private static string InternalName(Item item) => item.ModItem?.Name ?? ItemID.Search.GetName(item.type) ?? "";
    private static int FamilyPrefix(Item item, Item selected)
    {
        string a = InternalName(item), b = InternalName(selected); int length = 0;
        while (length < Math.Min(a.Length, b.Length) && a[length] == b[length]) length++;
        return length;
    }
    private static IEnumerable<(Item Head, Item Body, Item Legs)> Candidates(Item selected)
    {
        if (_heads == null)
        {
            var items = ContentSamples.ItemsByType.Values.Where(IsArmor).ToArray();
            var air = new[] { ContentSamples.ItemsByType[0] };
            _heads = items.Where(i => i.headSlot >= 0).Concat(air).ToArray();
            _bodies = items.Where(i => i.bodySlot >= 0).Concat(air).ToArray();
            _legs = items.Where(i => i.legSlot >= 0).Concat(air).ToArray();
        }
        IEnumerable<Item> heads = selected.headSlot >= 0 ? new[] { selected } : _heads;
        IEnumerable<Item> bodies = selected.bodySlot >= 0 ? new[] { selected } : _bodies;
        IEnumerable<Item> legs = selected.legSlot >= 0 ? new[] { selected } : _legs;
        Item[] Order(IEnumerable<Item> values) => values.OrderBy(i => i.ModItem?.Mod != selected.ModItem?.Mod)
            .ThenByDescending(i => FamilyPrefix(i, selected)).ToArray();
        var orderedHeads = Order(heads); var orderedBodies = Order(bodies); var orderedLegs = Order(legs);
        foreach (var head in orderedHeads) foreach (var body in orderedBodies) foreach (var leg in orderedLegs) yield return (head, body, leg);
    }
    internal static void Advance(int maximum = 256)
    {
        if (!Queries.TryGetValue(_priority, out var query) || query.Complete) return;
        long started = Stopwatch.GetTimestamp();
        for (int work = 0; work < maximum && Stopwatch.GetElapsedTime(started).TotalMilliseconds < 1.5; work++)
        {
            if (!query.Candidates.MoveNext()) { query.Complete = true; query.Candidates.Dispose(); break; }
            var (head, body, legs) = query.Candidates.Current;
            if (head.ModItem == null && body.ModItem == null && legs.ModItem == null)
            { Add(head, body, legs, ArmorSetText.Read(head, body, legs)); continue; }
            Match(head.ModItem, head, body, legs);
            if (!ReferenceEquals(body.ModItem, head.ModItem)) Match(body.ModItem, head, body, legs);
            if (!ReferenceEquals(legs.ModItem, head.ModItem) && !ReferenceEquals(legs.ModItem, body.ModItem)) Match(legs.ModItem, head, body, legs);
        }
    }
    private static void Match(ModItem owner, Item head, Item body, Item legs)
    {
        if (owner == null || Failed.Contains(owner.GetType())) return;
        try
        {
            // IsArmorSet is the engine's read-only matching query. UpdateArmorSet is not called.
            if (!owner.IsArmorSet(head, body, legs)) return;
            string text = ArmorSetText.Read(head, body, legs, owner);
            Add(head, body, legs, text ?? "该套装没有可安全读取的基础说明；穿戴后可查看实时效果");
        }
        catch (Exception ex)
        { Failed.Add(owner.GetType()); AE2ForTerraria.Instance?.Logger.Warn("Armor set query failed for " + owner.FullName + ": " + ex); }
    }
    private static void Add(Item head, Item body, Item legs, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        void AddTo(Item item)
        {
            if (item.IsAir) return;
            if (!Descriptions.TryGetValue(item.type, out var descriptions)) Descriptions[item.type] = descriptions = new();
            descriptions.Add(text);
        }
        AddTo(head); AddTo(body); AddTo(legs);
    }
    internal static void Reset()
    {
        foreach (var query in Queries.Values) query.Candidates.Dispose();
        Queries.Clear(); Descriptions.Clear(); Failed.Clear(); _heads = _bodies = _legs = null; _priority = 0; _culture = null; ArmorSetText.Reset();
    }
}
