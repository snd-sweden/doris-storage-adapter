using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.Runtime.CompilerServices;

namespace DorisStorageAdapter.Services.Implementation.Services.Validation;

internal sealed class DatasetVersionValidator(
    IOptions<SystemConfiguration> configuration)
{
    private readonly SystemConfiguration _configuration = configuration.Value;

    public void ThrowIfInvalid(
        DatasetVersion datasetVersion,
        [CallerArgumentExpression(nameof(datasetVersion))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);

        ValidatePart(datasetVersion.Identifier, paramName);
        ValidatePart(datasetVersion.Version, paramName);
        ValidateTenant(datasetVersion.TenantId, paramName);
    }

    private void ValidateTenant(string? tenantId, string? paramName)
    {
        if (_configuration.EnableTenancy)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new ValidationException([new(
                    Target: paramName,
                    Message: "Tenant id is required when tenancy is enabled.")]);
            }

            ValidatePart(tenantId, paramName);
        }
        else if (tenantId is not null)
        {
            throw new ValidationException([new(
                Target: paramName,
                Message: "Tenant id is not supported when tenancy is disabled.")]);
        }
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
