using DorisStorageAdapter.Services.Contract.Models;
using System;

namespace DorisStorageAdapter.Server.Controllers.Models;

public record File(
    long ContentSize,
    DateTime? DateCreated,
    DateTime? DateModified,
    string EncodingFormat,
    string Name,
    string? Sha256,
    FileType Type);
