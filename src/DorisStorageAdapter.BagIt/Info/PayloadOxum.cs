using System;

namespace DorisStorageAdapter.BagIt.Info;

public sealed record PayloadOxum
{
    public long OctetCount { get; }
    public long StreamCount { get; }

    public PayloadOxum(long octetCount, long streamCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(octetCount);
        ArgumentOutOfRangeException.ThrowIfNegative(streamCount);

        OctetCount = octetCount;
        StreamCount = streamCount;
    }
}