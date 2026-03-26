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
    FileType Type)
{
    public static File FromFileMetadata(FileMetadata file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new(
            ContentSize: file.Size,
            DateCreated: file.DateCreated,
            DateModified: file.DateModified,
            EncodingFormat: file.ContentType,
            Name: file.Path,
            Sha256: file.Sha256 == null 
                ? null 
                : Convert.ToHexStringLower(file.Sha256),
            Type: file.Type
        );
    }
}
