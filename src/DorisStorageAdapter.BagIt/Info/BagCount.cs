using System;

namespace DorisStorageAdapter.BagIt.Info;

public sealed record BagCount
{
    public long Ordinal { get; }
    public long? TotalCount { get; }

    public BagCount(long ordinal, long? totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        if (totalCount != null)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(totalCount.Value);
        }

        Ordinal = ordinal;
        TotalCount = totalCount;
    }
}
