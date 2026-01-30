using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using DorisStorageAdapter.Services.Implementation.Lock;
using DorisStorageAdapter.Services.Implementation.Storage;
using Microsoft.Extensions.Options;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation;

internal sealed class StorageLimitsService(
    IStorageService storageService,
    ILockService lockService,
    IOptions<StorageLimitsConfiguration> limitsConfiguration) : IStorageLimitsService
{
    private readonly IStorageService storageService = storageService;
    private readonly ILockService lockService = lockService;
    private readonly StorageLimitsConfiguration limitsConfiguration = limitsConfiguration.Value;

    public async Task<StorageLimits> GetStorageLimits(
    DatasetVersion datasetVersion,
    CancellationToken cancellationToken)
    {
        StorageLimitsConfiguration? config = null;

        var limitsFile = await storageService.GetData(
            Paths.GetDatasetPath(datasetVersion) + "limits.json", null, cancellationToken);

        if (limitsFile != null)
        {
            // Felhantering, vad göra om filen inte kan parseas etc.

            using (limitsFile.Stream)
            {
                config = await JsonSerializer.DeserializeAsync<StorageLimitsConfiguration>(
                    limitsFile.Stream, cancellationToken: cancellationToken);
            }
        }

        config ??= limitsConfiguration;

        return new(
               MaxFileCount: config.MaxFileCount,
               MaxFileSize: config.MaxFileSize,
               MaxTotalSize: config.MaxTotalSize);
    }

    public async Task SetStorageLimits(
        DatasetVersion datasetVersion,
        StorageLimits storageLimits,
        CancellationToken cancellationToken)
    {
        string filePath = Paths.GetDatasetPath(datasetVersion) + "limits.json";

        using (await lockService.LockPath(filePath, cancellationToken))
        {
            using var stream = new MemoryStream();

            await JsonSerializer.SerializeAsync(
                stream,
                new StorageLimitsConfiguration
                {
                    MaxFileCount = storageLimits.MaxFileCount,
                    MaxFileSize = storageLimits.MaxFileSize,
                    MaxTotalSize = storageLimits.MaxTotalSize
                },
                cancellationToken: cancellationToken);

            stream.Position = 0;

            await storageService.Store(
                filePath,
                stream,
                stream.Length,
                "application/json",
                cancellationToken);
        }
    }
}
