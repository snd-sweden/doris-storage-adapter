using System;
using System.IO;

namespace DorisStorageAdapter.Services.Implementation.Storage;

internal static class StreamHelpers
{
    /// <summary>
    /// Wraps a seekable stream and returns a stream that only contains the slice
    /// specified in the given byte range.
    /// </summary>
    /// <param name="stream">The stream to wrap.</param>
    /// <param name="byteRange">The byte range.</param>
    /// <returns></returns>
    public static Stream CreateByteRangeStream(Stream stream, StorageByteRange byteRange)
    {
        long endPosition = stream.Length;

        if (byteRange.From == null)
        {
            stream.Position = Math.Max(
                stream.Length - byteRange.To.GetValueOrDefault(), 0);
        }
        else
        {
            stream.Position = Math.Min(byteRange.From.Value, stream.Length);

            if (byteRange.To != null)
            {
                // Avoid overflow when To == long.MaxValue.
                endPosition = byteRange.To.Value >= stream.Length
                    ? stream.Length
                    : byteRange.To.Value + 1;
            }
        }

        return new ReadSliceStream(stream, endPosition - stream.Position);
    }
}
