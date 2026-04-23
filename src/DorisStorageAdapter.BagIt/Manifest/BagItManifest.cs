using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Manifest;

public abstract class BagItManifest<T> where T : BagItManifest<T>, IBagItElement<T>
{
    private readonly SortedDictionary<string, BagItManifestItem> _items = [];
    private readonly Dictionary<Checksum, Dictionary<string, BagItManifestItem>> _checksumToItems = [];

    public IEnumerable<BagItManifestItem> Items => _items.Values;

    public bool AddOrUpdateItem(BagItManifestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

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

    protected static async Task<T> ParseCoreAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = T.CreateEmpty();

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? line;
        int lineNumber = 0;
        int? firstEmptyLineNumber = null;

        static void ThrowParseException(int lineNumber, string text) =>
              throw new BagItParseException($"Invalid manifest item at line {lineNumber}: {text}");

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

            int startIndex = 0;

            while (startIndex < line.Length && line[startIndex] != ' ' && line[startIndex] != '\t')
            {
                startIndex++;
            }

            if (startIndex == line.Length)
            {
                ThrowParseException(lineNumber,
                   "Expected '<checksum><linear whitespace><file path>' but no linear whitespace was found.");
            }

            if (startIndex < 2)
            {
                ThrowParseException(lineNumber,
                    "Expected '<checksum><linear whitespace><file path>' but no checksum was found.");
            }

            int endIndex = startIndex;
            while (endIndex < line.Length && (line[endIndex] == ' ' || line[endIndex] == '\t'))
            {
                endIndex++;
            }

            if (endIndex == line.Length)
            {
                ThrowParseException(lineNumber,
                    "Expected '<checksum><linear whitespace><file path>' but no file path was found.");
            }

            Checksum checksum;

            try
            {
                checksum = Checksum.ParseHexString(line[..startIndex]);
            }
            catch (Exception e) when (e is FormatException or ArgumentException)
            {
                ThrowParseException(lineNumber, e.Message);
            }

            string filePath;

            try
            {
                filePath = BagItPathCodec.DecodeFilePath(line[endIndex..]);
            }
            catch (FormatException e)
            {
                ThrowParseException(lineNumber, e.Message);
            }

            if (result.Contains(filePath))
            {
                ThrowParseException(lineNumber, "Duplicate file path {filePath} found.");
            }

            result.AddOrUpdateItem(new(filePath, checksum));
        }

        if (!result.HasValues())
        {
            throw new BagItParseException("Invalid manifest file: File contains no entries.");
        }

        return result;
    }

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
