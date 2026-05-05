using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Services.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Mime;

namespace DorisStorageAdapter.Server.Controllers;

[ApiController]
public sealed class SystemController(
    ISystemService systemService,
    IOptions<SecurityConfiguration> securityConfiguration) : ControllerBase
{
    private readonly ISystemService _systemService = systemService;

    [HttpGet("system/information")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<SystemInformation>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    public SystemInformation GetInformation()
    {
        var info = _systemService.GetSystemInformation();

        return new(
            AllowReadDraftFiles: securityConfiguration.Value.AllowReadDraftFiles,
            DatasetAccessMode: info.DatasetAccessMode,
            StorageProvider: info.StorageProvider,
            TenancyEnabled: info.TenancyEnabled,
            Version: ApplicationInfo.Version);
    }
}
