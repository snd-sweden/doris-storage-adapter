using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Manifest;

public abstract class BagItManifest<T>(ChecksumAlgorithm algorithm) : IBagItElement
    where T : BagItManifest<T>
{
    private readonly SortedDictionary<string, BagItManifestItem> _items = [];
    private readonly Dictionary<Checksum, Dictionary<string, BagItManifestItem>> _checksumToItems = [];

    public ChecksumAlgorithm Algorithm { get; } = algorithm;
 
    public IEnumerable<BagItManifestItem> Items => _items.Values;

    public bool AddOrUpdateItem(BagItManifestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Checksum.Algorithm != Algorithm)
        {
            throw new ArgumentException(
                $"Checksum algorithm mismatch. Manifest uses {Algorithm}, " +
                $"but item uses {item.Checksum.Algorithm}.",
                nameof(item));
        }

        if (TryGetItem(item.FilePath, out var existingItem))
        {
            if (item == existingItem)
            {
                return false;
            }

            RemoveItemFromChecksumDictionary(existingItem);
        }

        _items[item.FilePath] = item;

        if (_checksumToItems.TryGetValue(item.Checksum, out var values))
        {
            values.Add(item.FilePath, item);
        }
        else
        {
            _checksumToItems[item.Checksum] = new() 
            { 
                [item.FilePath] = item 
            };
        }

        return true;
    }

    public bool Contains(string filePath) => _items.ContainsKey(filePath);

    public IEnumerable<BagItManifestItem> GetItemsByChecksum(Checksum checksum)
    {
        if (_checksumToItems.TryGetValue(checksum, out var items))
        {
            return items.Values;
        }

        return [];
    }

    public bool RemoveItem(string filePath)
    {
        if (TryGetItem(filePath, out var item))
        {
            RemoveItemFromChecksumDictionary(item);
            _items.Remove(filePath);

            return true;
        }

        return false;
    }

    public bool TryGetItem(string filePath, [NotNullWhen(true)] out BagItManifestItem? item) => 
        _items.TryGetValue(filePath, out item);

    public bool HasValues() => Items.Any();

    public byte[] Serialize()
    {
        var builder = new StringBuilder();

        foreach (var item in Items)
        {
            builder.Append(item.Checksum.HexString);
            builder.Append(' ');
            builder.Append(BagItPathCodec.EncodeFilePath(item.FilePath));
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    protected static string BuildFileName(string prefix, ChecksumAlgorithm algo) =>
        $"{prefix}-{algo.ToBagItName()}.txt";

    protected static async Task<T> ParseCoreAsync(
        Stream stream, T result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        string? line;
        int lineNumber = 0;
        int? firstEmptyLineNumber = null;

        static void ThrowParseException(int lineNumber, string text) =>
              throw new BagItParseException($"Invalid manifest entry at line {lineNumber}: {text}");

        (string ChecksumHexString, string EncodedFilePath) SplitLine()
        {
            void Throw() =>
                ThrowParseException(
                    lineNumber,
                    $"Expected '<checksum><linear whitespace><file path>'.");

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

            return (line[..first], line[secondStart..]);
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

            (string checksumHexString, string filePath) = SplitLine();

            Checksum checksum;

            try
            {
                checksum = Checksum.ParseHexString(result.Algorithm, checksumHexString);
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                ThrowParseException(lineNumber, e.Message);
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
                ThrowParseException(lineNumber, $"Duplicate file path {filePath} found.");
            }

            result.AddOrUpdateItem(new(filePath, checksum));
        }

        if (!result.HasValues())
        {
            throw new BagItParseException("Invalid manifest file: File contains no entries.");
        }

        return result;
    }

    private void RemoveItemFromChecksumDictionary(BagItManifestItem item)
    {
        var dictionary = _checksumToItems[item.Checksum];
        dictionary.Remove(item.FilePath);
        if (dictionary.Count == 0)
        {
            _checksumToItems.Remove(item.Checksum);
        }
    }
}
