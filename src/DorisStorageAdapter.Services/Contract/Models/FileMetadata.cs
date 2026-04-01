using System;

namespace DorisStorageAdapter.Services.Contract.Models;

public sealed record FileMetadata(
    string ContentType,
    DateTime? DateCreated,
    DateTime? DateModified,
#pragma warning disable CA1819 // Properties should not return arrays
    byte[]? Sha256,
#pragma warning restore CA1819
    string Path,
    long Size);