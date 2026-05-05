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

        if (!IsValid(datasetVersion))
        {
            throw new ValidationException(
                [new(Message: "Invalid dataset version or tenant id.")]);
        }
    }

    public bool IsValid(DatasetVersion datasetVersion)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);

        return
            PathValidation.IsValidComponent(datasetVersion.Identifier) &&
            PathValidation.IsValidComponent(datasetVersion.Version) &&
            IsValidTenant(datasetVersion.TenantId);
    }

    private bool IsValidTenant(string? tenantId)
    {
        if (_configuration.EnableTenancy)
        {
            return
                !string.IsNullOrWhiteSpace(tenantId) &&
                PathValidation.IsValidComponent(tenantId);
        }

        return tenantId is null;
    }
}
