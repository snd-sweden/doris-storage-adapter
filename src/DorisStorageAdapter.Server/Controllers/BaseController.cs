using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Mvc;
using System;

namespace DorisStorageAdapter.Server.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    protected bool CheckClaims(DatasetVersion datasetVersion)
    {
        ArgumentNullException.ThrowIfNull(datasetVersion);
        return Claims.CheckClaims(datasetVersion, User.Claims);
    }
}
