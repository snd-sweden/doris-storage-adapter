using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Services.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net.Mime;

namespace DorisStorageAdapter.Server.Controllers;

[ApiController]
public sealed class SystemController(ISystemService systemService) : ControllerBase
{
    private readonly ISystemService systemService = systemService;

    private readonly static string version = FileVersionInfo
        .GetVersionInfo(typeof(SystemController).Assembly.Location)
        .ProductVersion ?? "";

    [HttpGet("system/information")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<Models.SystemInformation>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    public Models.SystemInformation GetInformation()
    {
        var info = systemService.GetSystemInformation();

        return new(
            StorageType: info.StorageType, 
            Version: version);
    }
}
