using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using System;

namespace DorisStorageAdapter.Services.Implementation.Services;

internal static class Validation
{
    public static void ThrowIfInvalidDatasetVersion(DatasetVersion datasetVersion)
    {
        static void ThrowIfInvalid(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Contains('/', StringComparison.Ordinal) ||
                value == "." ||
                value == "..")
            {
                throw new ValidationException([new("Invalid dataset version.")]);
            }
        }

        ThrowIfInvalid(datasetVersion.Identifier);
        ThrowIfInvalid(datasetVersion.Version);
    }
}
