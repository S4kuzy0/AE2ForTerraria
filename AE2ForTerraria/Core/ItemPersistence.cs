using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader.IO;
using AE2ForTerraria.Systems;

namespace AE2ForTerraria.Core;

/// <summary>One lossless identity codec for storage, save files and item reconstruction.</summary>
public static class ItemPersistence
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, List<TagCompound>> MissingFilters = new();
    public static bool HasMissingFilterReferences(object filter)
        => filter!=null && MissingFilters.TryGetValue(filter,out var missing) && missing.Count>0;
    public static void SaveFilter(TagCompound tag, string name, IEnumerable<int> types)
    {
        var items = new List<TagCompound>();
        if (types != null)
        {
            foreach (int type in types)
                if (SavedItemGuard.IsValidType(type)) items.Add(ItemIO.Save(new Item(type)));
            if (MissingFilters.TryGetValue(types, out var missing)) items.AddRange(missing);
        }
        tag[name + "Items"] = items;
    }
    public static List<int> LoadFilter(TagCompound tag, string name)
    {
        var types = new List<int>();
        if (tag.ContainsKey(name + "Items"))
        {
            foreach (var saved in tag.GetList<TagCompound>(name + "Items"))
            {
                Item item = ItemIO.Load(saved);
                if (item.ModItem is Terraria.ModLoader.Default.UnloadedItem)
                    MissingFilters.GetOrCreateValue(types).Add(saved);
                else if (!item.IsAir && !types.Contains(item.type)) types.Add(item.type);
            }
        }
        else if (tag.ContainsKey(name))
            foreach (int type in tag.GetList<int>(name))
                if (SavedItemGuard.IsValidType(type) && !types.Contains(type)) types.Add(type);
        return types;
    }
    public static void CopyMissingFilterReferences(object source, object target)
    {
        if (source == null || target == null || ReferenceEquals(source, target)
            || !MissingFilters.TryGetValue(source, out var missing)) return;
        var destination = MissingFilters.GetOrCreateValue(target);
        var identities = new HashSet<string>(destination.Select(FilterIdentity), StringComparer.Ordinal);
        foreach (var entry in missing)
            if (identities.Add(FilterIdentity(entry))) destination.Add(entry);
    }

    /// <summary>A new empty collection explicitly clears a filter; copies retain unavailable references.</summary>
    public static List<int> CloneFilter(IEnumerable<int> source)
    {
        var copy = source?.Where(SavedItemGuard.IsValidFilterEntry).Distinct().ToList() ?? new();
        CopyMissingFilterReferences(source, copy);
        return copy;
    }

    public static void ReplaceFilterContents(List<int> target, IEnumerable<int> source)
    {
        var replacement = CloneFilter(source);
        target.Clear();
        MissingFilters.Remove(target);
        target.AddRange(replacement);
        CopyMissingFilterReferences(replacement, target);
    }

    public static bool TryToggleFilterType(List<int> filter, int type)
    {
        if (filter == null) return false;
        // Explicit removal can repair an old invalid selection; additions require an original identity.
        if (filter.Remove(type)) return true;
        if (!SavedItemGuard.IsSelectableFilterType(type)) return false;
        filter.Add(type);
        return true;
    }

    private static string FilterIdentity(TagCompound tag)
        => tag.GetString("mod") == "Terraria" ? tag.GetInt("id").ToString(System.Globalization.CultureInfo.InvariantCulture)
            : tag.GetString("mod") + "/" + tag.GetString("name");

    public static List<string> FilterIdentities(IEnumerable<int> source)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (source != null)
        {
            foreach (int type in source)
                if (SavedItemGuard.IsValidFilterEntry(type))
                    result.Add(Terraria.ModLoader.ItemLoader.GetItem(type)?.FullName
                        ?? type.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (MissingFilters.TryGetValue(source, out var missing))
                foreach (var tag in missing) result.Add(FilterIdentity(tag));
        }
        return result.OrderBy(identity => identity, StringComparer.Ordinal).ToList();
    }

    public static List<int> FilterFromIdentities(IEnumerable<string> identities)
    {
        var result = new List<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var resolvedTypes = new HashSet<int>();
        foreach (string identity in identities ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(identity)) throw new InvalidDataException("Empty filter item identity");
            if (!seen.Add(identity)) continue;
            if (int.TryParse(identity, out int type))
            {
                // Mod numeric IDs can point to a different item after reload. Never guess their identity.
                if (type <= 0 || type >= Terraria.ID.ItemID.Count)
                    throw new InvalidDataException("Numeric filter identities must reference vanilla items");
                if (resolvedTypes.Add(type)) result.Add(type);
                continue;
            }
            int slash = identity.IndexOf('/');
            if (slash <= 0 || slash == identity.Length - 1 || identity.IndexOf('/', slash + 1) >= 0)
                throw new InvalidDataException("Invalid filter item identity");
            if (identity.Substring(0, slash) == "Terraria")
            {
                if (!Terraria.ID.ItemID.Search.TryGetId(identity.Substring(slash + 1), out type)
                    || type <= 0 || type >= Terraria.ID.ItemID.Count)
                    throw new InvalidDataException("Unknown vanilla filter item identity");
                if (resolvedTypes.Add(type)) result.Add(type);
                continue;
            }
            if (Terraria.ModLoader.ModContent.TryFind<Terraria.ModLoader.ModItem>(identity, out var item))
            {
                if (item is Terraria.ModLoader.Default.UnloadedItem)
                    throw new InvalidDataException("A missing-item placeholder is not an original item identity");
                if (resolvedTypes.Add(item.Type)) result.Add(item.Type);
                continue;
            }
            // Store unresolved names without constructing items or invoking another mod's save hooks.
            MissingFilters.GetOrCreateValue(result).Add(new TagCompound
            {
                ["mod"] = identity.Substring(0, slash), ["name"] = identity.Substring(slash + 1), ["stack"] = 1
            });
        }
        return result;
    }
    private static TagCompound Normalized(Item item)
    {
        Item copy = item.Clone();
        copy.stack = 1;
        copy.favorited = false;
        return ItemIO.Save(copy);
    }

    private static TagCompound Sort(TagCompound tag)
    {
        var sorted = new TagCompound();
        foreach (string key in tag.Select(pair => pair.Key).OrderBy(k => k, StringComparer.Ordinal))
        {
            object value = tag[key];
            sorted[key] = value is TagCompound child ? Sort(child)
                : value is IList<TagCompound> children ? children.Select(Sort).ToList() : value;
        }
        return sorted;
    }

    private static string Encode(TagCompound tag)
    {
        using var stream = new MemoryStream();
        TagIO.ToStream(Sort(tag), stream, false);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static TagCompound Decode(string data)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(data));
        return TagIO.FromStream(stream, false);
    }

    public static ItemKey KeyOf(Item item)
    {
        if (item == null || item.IsAir) return ItemKey.None;
        string data = Encode(Normalized(item));
        // A bare UnloadedItem has no original tag; its SaveData cannot be called until ItemIO.Load fills it.
        if (item.ModItem is Terraria.ModLoader.Default.UnloadedItem)
            return new ItemKey(item.type, item.prefix, 0, data);
        var plain = new Item(item.type);
        if (item.prefix > 0) plain.Prefix(item.prefix);
        // Ordinary items retain compact keys and match recipe ingredients directly.
        if (data == Encode(Normalized(plain))) data = null;
        return new ItemKey(item.type, item.prefix, 0, data);
    }

    public static Item Create(ItemKey key, int stack = 1)
    {
        if (key.IsAir) return new Item();
        Item item;
        if (key.Data != null) item = ItemIO.Load(Decode(key.Data));
        else
        {
            item = new Item(key.Type);
            if (key.Prefix > 0) item.Prefix(key.Prefix);
        }
        item.stack = Math.Max(1, stack);
        return item;
    }

    public static void WriteKey(TagCompound tag, ItemKey key, string name = "item")
        => tag[name] = ItemIO.Save(Create(key));

    public static ItemKey ReadKey(TagCompound tag, string name = "item", string type = "type", string prefix = "prefix")
    {
        if (tag.ContainsKey(name)) return KeyOf(ItemIO.Load(tag.GetCompound(name)));
        return SavedItemGuard.TryRead(TagCompat.ReadInt(tag, type), TagCompat.ReadInt(tag, prefix), 1, out var key)
            ? key : ItemKey.None;
    }

    public static List<TagCompound> SaveLedger(StorageLedger ledger)
    {
        var entries = new List<TagCompound>();
        foreach (var pair in ledger.Entries)
        {
            var tag = new TagCompound { ["amount"] = pair.Value };
            WriteKey(tag, pair.Key);
            entries.Add(tag);
        }
        return entries;
    }

    public static void LoadLedger(StorageLedger ledger, TagCompound tag)
    {
        var decoded = new StorageLedger(ledger.BytesPerType, ledger.AmountPerByte, ledger.MaxTypes)
            { MaxBytes = ledger.MaxBytes };
        if (tag.ContainsKey("entries"))
        {
            foreach (var entry in tag.GetList<TagCompound>("entries"))
            {
                ItemKey key = ReadKey(entry);
                long amount = TagCompat.ReadLong(entry, "amount");
                if (!key.IsAir && amount > 0) decoded.Restore(key, amount);
            }
        }
        ledger.ReplaceContents(decoded);
    }
}
