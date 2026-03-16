using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace DorisStorageAdapter.Services.Implementation.Storage.InMemory;

internal sealed class InMemoryStorage
{
    private readonly ConcurrentDictionary<string, InMemoryFile> _files = new(StringComparer.Ordinal);

    public InMemoryFile AddOrUpdate(string filePath, byte[] data, string? contentType) =>
        _files.AddOrUpdate(filePath,
            new InMemoryFile(new(
                ContentType: contentType,
                DateCreated: DateTime.UtcNow,
                DateModified: DateTime.UtcNow,
                Size: data.Length,
                Path: filePath),
                data),
            (_, oldValue) =>
                new(oldValue.Metadata with
                {
                    ContentType = contentType,
                    DateModified = DateTime.UtcNow,
                    Size = data.LongLength
                },
                data));


    public void Remove(string filePath) => _files.TryRemove(filePath, out var _);

    public bool TryGet(string filePath, [NotNullWhen(true)] out InMemoryFile? file) =>
        _files.TryGetValue(filePath, out file);

    public IEnumerable<InMemoryFile> ListFiles(string path) =>
        _files.Where(f => f.Key.StartsWith(path, StringComparison.Ordinal)).Select(f => f.Value);
}
