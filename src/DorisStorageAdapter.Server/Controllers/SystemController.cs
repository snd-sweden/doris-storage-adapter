using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Server.Controllers.Models;
using DorisStorageAdapter.Services.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Mime;

namespace DorisStorageAdapter.Server.Controllers;

public sealed class SystemController(
    ISystemService systemService,
    IOptions<AuthorizationConfiguration> authorizationConfiguration) : BaseController
{
    private readonly ISystemService _systemService = systemService;

    private readonly static string version = FileVersionInfo
        .GetVersionInfo(typeof(SystemController).Assembly.Location)
        .ProductVersion ?? "";

    [HttpGet("system/information")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<SystemInformation>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    public SystemInformation GetInformation()
    {
        var info = _systemService.GetSystemInformation();

        return new(
            AllowPublicAccessRight: info.AllowPublicAccessRight,
            AllowReadDraftFiles: authorizationConfiguration.Value.AllowReadDraftFiles,
            StorageProvider: info.StorageProvider, 
            Version: version);
    }
}
