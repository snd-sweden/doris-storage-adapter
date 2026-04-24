using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Fetch;

public sealed class BagItFetch : IBagItElement<BagItFetch>
{
    private readonly SortedDictionary<string, BagItFetchItem> _items = [];

    public IEnumerable<BagItFetchItem> Items => _items.Values;

    public bool AddOrUpdateItem(BagItFetchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (TryGetItem(item.FilePath, out var existingItem) &&
            item == existingItem)
        {
            return false;
        }

        _items[item.FilePath] = item;

        return true;
    }

    public bool Contains(string filePath) => _items.ContainsKey(filePath);

    public bool RemoveItem(string filePath) => _items.Remove(filePath);

    public bool TryGetItem(string filePath, [NotNullWhen(true)] out BagItFetchItem? item) =>
        _items.TryGetValue(filePath, out item);

    public static BagItFetch CreateEmpty() => new();

    public static string FileName => "fetch.txt";

    public bool HasValues() => Items.Any();

    public static async Task<BagItFetch> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new BagItFetch();

        string? line;
        int lineNumber = 0;
        int? firstEmptyLineNumber = null;

        static void ThrowParseException(int lineNumber, string text) =>
              throw new BagItParseException($"Invalid fetch entry at line {lineNumber}: {text}");

        (string UrlText, string LengthText, string EncodedFilePath) SplitLine()
        {
            void Throw() =>
                ThrowParseException(
                    lineNumber, 
                    $"Expected '<url><linear whitespace><length><linear whitespace><file path>'.");

            int first = BagItParsing.FindLinearWhiteSpaceIndex(line);
            if (first <= 0)
            {
                Throw();
            }

            int secondStart = BagItParsing.SkipLinearWhiteSpace(line, first);
            if (secondStart == line.Length)
            {
                Throw();
            }

            int second = BagItParsing.FindLinearWhiteSpaceIndex(line, secondStart);
            if (second < 0)
            {
                Throw();
            }

            int thirdStart = BagItParsing.SkipLinearWhiteSpace(line, second);
            if (thirdStart == line.Length)
            {
                Throw();
            }

            return (line[..first], line[secondStart..second], line[thirdStart..]);
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            lineNumber++;

            if (line.Length == 0)
            {
                firstEmptyLineNumber ??= lineNumber;
                continue;
            }

            if (firstEmptyLineNumber != null)
            {
                ThrowParseException(firstEmptyLineNumber.Value,
                    "Empty lines are only allowed at the end of the file.");
            }

            (string url, string lengthString, string filePath) = SplitLine();

            long length = -1;

            if (lengthString != "-" &&
                (!long.TryParse(lengthString, out length) ||
                length < 0))
            {
                ThrowParseException(lineNumber, "Invalid length.");
            }

            try
            {
                filePath = BagItPathCodec.DecodeFilePath(filePath);
            }
            catch (FormatException e)
            {
                ThrowParseException(lineNumber, e.Message);
            }

            if (result.Contains(filePath))
            {
                ThrowParseException(lineNumber, "Duplicate file path {filePath} found.");
            }

            result.AddOrUpdateItem(new(filePath, length < 0 ? null : length, url));
        }

        if (!result.HasValues())
        {
            throw new BagItParseException("Invalid fetch file: File contains no entries.");
        }

        return result;
    }

    public byte[] Serialize()
    {
        var builder = new StringBuilder();

        foreach (var item in Items)
        {
            builder.Append(item.Url);
            builder.Append(' ');
            builder.Append(item.Length?.ToString(CultureInfo.InvariantCulture) ?? "-");
            builder.Append(' ');
            builder.Append(BagItPathCodec.EncodeFilePath(item.FilePath));
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
