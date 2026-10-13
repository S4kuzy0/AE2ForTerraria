using System.Collections.Generic;

namespace AE2ForTerraria.Core;

public enum StorageTransactionFailure
{
    None,
    NoStorage,
    IngredientsChanged,
    OutputRejected
}

/// <summary>Validate on private ledgers, then commit on the game thread. No hooks run between validation and commit.</summary>
public static class StorageTransaction
{
    public static bool TryApply(List<IMEStorage> storage, IReadOnlyDictionary<ItemKey, long> consumed,
        IReadOnlyDictionary<ItemKey, long> produced, out string reason)
        => TryApply(storage, consumed, produced, out reason, out _);

    public static bool TryApply(List<IMEStorage> storage, IReadOnlyDictionary<ItemKey, long> consumed,
        IReadOnlyDictionary<ItemKey, long> produced, out string reason, out StorageTransactionFailure failure)
    {
        reason = null;
        failure = StorageTransactionFailure.None;
        if (storage == null || storage.Count == 0)
        { reason = "没有连接的存储"; failure = StorageTransactionFailure.NoStorage; return false; }
        var copies = new List<StorageLedger>();
        var unique = new List<IMEStorage>();
        foreach (var unit in storage)
        {
            if (unique.Exists(u => ReferenceEquals(u.Ledger, unit.Ledger))) continue;
            unique.Add(unit);
            copies.Add(unit.Ledger.Copy());
        }
        foreach (var entry in consumed)
        {
            long left = entry.Value;
            foreach (var ledger in copies) left -= ledger.Extract(entry.Key, left);
            if (left != 0)
            { reason = "原料库存已变化，正在重新规划"; failure = StorageTransactionFailure.IngredientsChanged; return false; }
        }
        foreach (var entry in produced)
        {
            long left = entry.Value;
            for (int i = 0; i < unique.Count && left > 0; i++)
                if (unique[i].Allows(entry.Key)) left -= copies[i].Insert(entry.Key, left);
            if (left != 0)
            { reason = "产物空间不足或被分区过滤，请腾出容量后重试"; failure = StorageTransactionFailure.OutputRejected; return false; }
        }
        for (int i = 0; i < unique.Count; i++)
        {
            var ledger = unique[i].Ledger;
            ledger.Clear();
            foreach (var entry in copies[i].Entries) ledger.Restore(entry.Key, entry.Value);
        }
        return true;
    }
}
