using System;
using System.Collections.Generic;
using AE2ForTerraria.Core;

namespace AE2ForTerraria.Systems;

/// <summary>Immutable-to-callers aggregates, invalidated by exact ledger identity and revision, never by a lossy hash.</summary>
public static class NetworkInventory
{
    private sealed class Snapshot
    {
        public StorageLedger[] Ledgers;
        public int[] Versions;
        public Dictionary<ItemKey,long> Contents;
    }
    private static readonly Dictionary<StorageLedger,Snapshot> Cache = new();
    public static int BuildCount { get; private set; }
    public static void Reset() { Cache.Clear(); BuildCount=0; }
    public static Dictionary<ItemKey,long> Get(List<IMEStorage> units)
    {
        if (units == null || units.Count == 0) return new();
        var first = units[0].Ledger;
        if (Cache.TryGetValue(first,out var old) && old.Ledgers.Length == units.Count)
        {
            bool same=true;
            for (int i=0;i<units.Count;i++)
                if (!ReferenceEquals(old.Ledgers[i],units[i].Ledger) || old.Versions[i]!=units[i].Ledger.Version) { same=false; break; }
            if (same) return old.Contents;
        }
        var next=new Snapshot { Ledgers=new StorageLedger[units.Count], Versions=new int[units.Count], Contents=new() };
        for (int i=0;i<units.Count;i++)
        {
            var ledger=units[i].Ledger; next.Ledgers[i]=ledger; next.Versions[i]=ledger.Version;
            foreach (var pair in ledger.Entries)
                next.Contents[pair.Key]=StorageAmounts.Add(next.Contents.GetValueOrDefault(pair.Key),pair.Value);
        }
        if (Cache.Count>=64 && !Cache.ContainsKey(first)) Cache.Clear();
        Cache[first]=next; BuildCount++; return next.Contents;
    }
}
