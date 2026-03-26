using System.ComponentModel.DataAnnotations;
using System.IO;

namespace DorisStorageAdapter.Services.Implementation.Storage.FileSystem;

internal sealed record FileSystemStorageConfiguration
{
    [Required]
    public required string BasePath 
    { 
        get;
        init => field = GetFullPath(value);
    }

    public string TempFilePath 
    { 
        get; 
        init => field = GetFullPath(value);
    } = Path.GetTempPath();

    private static string GetFullPath(string value) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) +
        Path.DirectorySeparatorChar;
}
