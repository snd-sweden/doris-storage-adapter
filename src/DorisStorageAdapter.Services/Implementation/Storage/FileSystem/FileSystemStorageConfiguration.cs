using System.ComponentModel.DataAnnotations;
using System.IO;

namespace DorisStorageAdapter.Services.Implementation.Storage.FileSystem;

internal sealed record FileSystemStorageConfiguration
{
    [Required]
    public required string BasePath { get; init; }

    public string TempFilePath { get; init; } = Path.GetTempPath();
}
