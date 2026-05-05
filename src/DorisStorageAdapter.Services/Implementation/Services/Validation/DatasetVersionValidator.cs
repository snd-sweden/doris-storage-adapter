using DorisStorageAdapter.Services.Contract.Exceptions;
using DorisStorageAdapter.Services.Contract.Models;
using DorisStorageAdapter.Services.Implementation.Configuration;
using Microsoft.Extensions.Options;
using System;

namespace DorisStorageAdapter.Services.Implementation.Services.Validation;

internal sealed class DatasetVersionValidator(
    IOptions<SystemConfiguration> configuration)
{
    private readonly SystemConfiguration _configuration = configuration.Value;

    public void ThrowIfInvalid(DatasetVersion datasetVersion)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);

        ValidatePart(datasetVersion.Identifier);
        ValidatePart(datasetVersion.Version);
        ValidateTenant(datasetVersion.TenantId);
    }

    private void ValidateTenant(string? tenantId)
    {
        if (_configuration.EnableTenancy)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new ValidationException([new(
                    Message: "Tenant id is required when tenancy is enabled.")]);
            }

            ValidatePart(tenantId);
        }
        else if (tenantId is not null)
        {
            throw new ValidationException([new(
                Message: "Tenant id is not supported when tenancy is disabled.")]);
        }
    }

    private static void ValidatePart(string value)
    {
        if (!PathValidation.IsValidComponent(value))
        {
            throw new ValidationException([new(
                Message: "Invalid dataset version.")]);
        }
    }
}
