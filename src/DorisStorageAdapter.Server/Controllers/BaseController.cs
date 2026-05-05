using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Tenancy;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Mvc;
using System;

namespace DorisStorageAdapter.Server.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    private readonly ITenantResolver _tenantResolver;

    protected BaseController(ITenantResolver tenantResolver)
    {
        ArgumentNullException.ThrowIfNull(tenantResolver);
        _tenantResolver = tenantResolver;
    }

    protected bool CheckClaims(DatasetVersion datasetVersion)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        return Claims.CheckClaims(datasetVersion, User.Claims);
    }

    protected DatasetVersion CreateDatasetVersion(string identifier, string version)
    {
        var tenantId = _tenantResolver.ResolveTenantId(HttpContext);
        return new DatasetVersion(identifier, version, tenantId);
    }
}
