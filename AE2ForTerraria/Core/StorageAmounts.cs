using System;
using System.Collections.Generic;

namespace AE2ForTerraria.Core;

/// <summary>Nonnegative quantity summaries saturate; authoritative ledger writes never discard overflow.</summary>
public static class StorageAmounts
{
    public const long MaxAmount = long.MaxValue;

    public static long Add(long left, long right)
    {
        if (left < 0 || right < 0) throw new ArgumentOutOfRangeException(nameof(right), "Storage amounts cannot be negative.");
        return right > long.MaxValue - left ? long.MaxValue : left + right;
    }

    public static long Sum(IEnumerable<long> values)
    {
        long total = 0;
        foreach (long value in values) total = Add(total, value);
        return total;
    }
}
