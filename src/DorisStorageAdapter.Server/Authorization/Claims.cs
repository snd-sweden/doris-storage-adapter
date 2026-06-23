using DorisStorageAdapter.Services.Contract.Models;
using System.Collections.Generic;
using System.Security.Claims;

namespace DorisStorageAdapter.Server.Authorization;

internal static class Claims
{
    public const string DatasetIdentifier = "dataset_identifier";
    public const string DatasetVersion = "dataset_version";
    public const string TenantId = "tenant_id";

    public static bool CheckClaims(DatasetVersion datasetVersion, IEnumerable<Claim> claims)
    {
        string? identifier = null;
        string? version = null;
        string? tenant = null;

        foreach (var claim in claims)
        {
            if (claim.Type == DatasetIdentifier)
            {
                if (identifier != null && identifier != claim.Value)
                {
                    return false;
                }

                identifier = claim.Value;
            }
            else if (claim.Type == DatasetVersion)
            {
                if (version != null && version != claim.Value)
                {
                    return false;
                }

                version = claim.Value;
            }
            else if (claim.Type == TenantId)
            {
                if (tenant != null && tenant != claim.Value)
                {
                    return false;
                }

                tenant = claim.Value;
            }
        }

        if (identifier != datasetVersion.Identifier)
        {
            return false;
        }

        if (version != datasetVersion.Version)
        {
            return false;
        }

        if (datasetVersion.TenantId != null &&
            tenant != datasetVersion.TenantId)
        {
            return false;
        }

        return true;
    }
}
