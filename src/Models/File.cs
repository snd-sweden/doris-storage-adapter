using System;

namespace DorisStorageAdapter.Models;

public record File
{
    public required string Name { get; init; }
    public required FileType Type { get; init; }
    public required long ContentSize { get; init; }
    public DateTime? DateCreated { get; init; }
    public DateTime? DateModified { get; init; }
    public required string EncodingFormat { get; init; }
    public string? Sha256 { get; init; }
    public required Uri Url { get; init; }
}