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
        if (MissingFilters.TryGetValue(source, out var missing))
            MissingFilters.GetOrCreateValue(target).AddRange(missing);
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
