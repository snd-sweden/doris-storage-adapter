﻿using System.ComponentModel.DataAnnotations;
using System.IO;

namespace DatasetFileUpload.Services.Storage.FileSystem;

internal record FileSystemStorageServiceConfiguration
{
    [Required]
    public required string BasePath { get; init; }

    public string TempFilePath { get; init; } = Path.GetTempPath();
}
