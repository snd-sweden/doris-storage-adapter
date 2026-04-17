using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using System.Runtime.CompilerServices;

namespace DorisStorageAdapter.Services.Implementation.Services.Validation;

internal static class DatasetVersionValidator
{
    public static void ThrowIfInvalid(
        DatasetVersion datasetVersion,
        [CallerArgumentExpression(nameof(datasetVersion))] string? paramName = null)
    {
        ValidatePart(datasetVersion.Identifier, paramName);
        ValidatePart(datasetVersion.Version, paramName);
    }

    private static void ValidatePart(string value, string? paramName)
    {
        if (!PathValidation.IsValidComponent(value))
        {
            throw new ValidationException([new(
                Target: paramName,
                Message: "Invalid dataset version.")]);
        }
    }
}
