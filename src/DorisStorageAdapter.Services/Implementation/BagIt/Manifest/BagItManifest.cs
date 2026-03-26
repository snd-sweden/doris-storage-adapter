using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.BagIt.Manifest;

internal abstract class BagItManifest<T> where T : BagItManifest<T>, new()
{
    private readonly SortedDictionary<string, BagItManifestItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<byte[], List<BagItManifestItem>> _checksumToItems = new(ByteArrayComparer.Default);

    public IEnumerable<BagItManifestItem> Items => _items.Values;

    public bool AddOrUpdateItem(BagItManifestItem item)
    {
        if (TryGetItem(item.FilePath, out var existingItem))
        {
            if (item == existingItem)
            {
                return false;
            }

            _checksumToItems[existingItem.Checksum].Remove(existingItem);
        }

        _items[item.FilePath] = item;

        if (_checksumToItems.TryGetValue(item.Checksum, out var values))
        {
            values.Add(item);
        }
        else
        {
            _checksumToItems[item.Checksum] = [item];
        }

        return true;
    }

    public bool Contains(string filePath) => _items.ContainsKey(filePath);

    public IEnumerable<BagItManifestItem> GetItemsByChecksum(byte[] checksum)
    {
        if (_checksumToItems.TryGetValue(checksum, out var items))
        {
            return items;
        }

        return [];
    }

    public bool RemoveItem(string filePath)
    {
        if (TryGetItem(filePath, out var item))
        {
            if (_checksumToItems.TryGetValue(item.Checksum, out var values))
            {
                values.Remove(item);
            }

            _items.Remove(filePath);

            return true;
        }

        return false;
    }

    public bool TryGetItem(string filePath, out BagItManifestItem item)
    {
        if (_items.TryGetValue(filePath, out var value))
        {
            item = value;
            return true;
        }

        item = new("", []);
        return false;
    }

    public bool HasValues() => Items.Any();

    public static async Task<T> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new T();

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
        {
            int index = line.IndexOf(' ', StringComparison.Ordinal);
            string checksum = line[..index];
            string filePath = BagItPathEncoder.DecodeFilePath(line[(index + 1)..]);

            result.AddOrUpdateItem(new(filePath, Convert.FromHexString(checksum)));
        }

        return result;
    }

    public byte[] Serialize()
    {
        var builder = new StringBuilder();

        foreach (var item in Items)
        {
            builder.Append(Convert.ToHexStringLower(item.Checksum));
            builder.Append(' ');
            builder.Append(BagItPathEncoder.EncodeFilePath(item.FilePath));
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
