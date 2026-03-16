using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.BagIt.Fetch;

internal sealed class BagItFetch : IBagItElement<BagItFetch>
{
    private readonly SortedDictionary<string, BagItFetchItem> _items = new(StringComparer.Ordinal);

    public IEnumerable<BagItFetchItem> Items => _items.Values;

    public bool AddOrUpdateItem(BagItFetchItem item)
    {
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

    public bool TryGetItem(string filePath, out BagItFetchItem item)
    {
        if (_items.TryGetValue(filePath, out var value))
        {
            item = value;
            return true;
        }

        item = new("", 0, "");
        return false;
    }
 
    public static BagItFetch CreateEmpty() => new();

    public static string FileName => "fetch.txt";

    public bool HasValues() => Items.Any();

    public static async Task<BagItFetch> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new BagItFetch();

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
        {
            int index1 = line.IndexOf(' ', StringComparison.Ordinal);
            string url = line[..index1];
            string remaining = line[(index1 + 1)..];
            int index2 = remaining.IndexOf(' ', StringComparison.Ordinal);
            long? length = long.TryParse(remaining[..index2], out long value) ? value : null;
            string filePath = BagItPathEncoder.DecodeFilePath(remaining[(index2 + 1)..]);

            result.AddOrUpdateItem(new(filePath, length, url));
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
            builder.Append(BagItPathEncoder.EncodeFilePath(item.FilePath));
            builder.Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
