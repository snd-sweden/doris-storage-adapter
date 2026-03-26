using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;

namespace DorisStorageAdapter.Services.Implementation.Services.Validation;

internal static class DatasetVersionValidator
{
    public static void ThrowIfInvalid(DatasetVersion datasetVersion)
    {
        ValidatePart(datasetVersion.Identifier);
        ValidatePart(datasetVersion.Version);
    }

    private static void ValidatePart(string value)
    {
        if (!PathValidation.IsValidComponent(value))
        {
            throw new ValidationException([new("Invalid dataset version.")]);
        }
    }
}
