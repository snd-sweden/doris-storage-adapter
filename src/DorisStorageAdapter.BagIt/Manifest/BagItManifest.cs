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
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
        {
            int index = line.IndexOf(' ', StringComparison.Ordinal);
            string checksum = line[..index];
            string filePath = BagItPathCodec.DecodeFilePath(line[(index + 1)..]);

            result.AddOrUpdateItem(new(filePath, Checksum.ParseHexString(checksum)));
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
