using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt;

internal static class BagItParsing
{
    private static readonly UTF8Encoding _strictUtf8Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static StreamReader CreateReader(Stream stream) =>
        new(
            stream: stream,
            encoding: _strictUtf8Encoding,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

    public static async Task<string?> ReadLineOrThrowAsync(
        StreamReader reader,
        int lineNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadLineAsync(cancellationToken);
        }
        catch (DecoderFallbackException ex)
        {
            throw new BagItParseException(
                $"Invalid UTF-8 near line {lineNumber}.",
                ex);
        }
    }

    public static int FindLinearWhiteSpaceIndex(string line, int start = 0)
    {
        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];
            if (c == ' ' || c == '\t')
            {
                return i;
            }
        }

        return -1;
    }

    public static int SkipLinearWhiteSpace(string line, int start)
    {
        int i = start;

        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            i++;
        }

        return i;
    }
}