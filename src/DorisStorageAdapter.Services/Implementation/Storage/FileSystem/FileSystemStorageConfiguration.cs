using System.ComponentModel.DataAnnotations;
using System.IO;

namespace DorisStorageAdapter.Services.Implementation.Storage.FileSystem;

internal sealed record FileSystemStorageConfiguration
{
    [Required]
    public required string BasePath 
    { 
        get;
        init => 
            field = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) +
                    Path.DirectorySeparatorChar;
    }
}
