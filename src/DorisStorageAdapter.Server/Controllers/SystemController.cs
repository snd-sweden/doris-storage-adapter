using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Services.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

[ApiController]
public sealed class SystemController(
    ISystemService systemService,
    IOptions<SecurityConfiguration> securityConfiguration,
    HealthCheckService healthCheckService) : ControllerBase
{
    private readonly ISystemService _systemService = systemService;
    private readonly HealthCheckService _healthCheckService = healthCheckService;

    [HttpGet("system/health/live")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, MediaTypeNames.Text.Plain)]
    [ProducesResponseType<string>(StatusCodes.Status503ServiceUnavailable, MediaTypeNames.Text.Plain)]
    public async Task<ContentHttpResult> Live(
        CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(
            _ => false,
            cancellationToken);

        return CreateHealthCheckResponse(report);
    }

    [HttpGet("system/health/ready")]
    [ProducesResponseType<string>(StatusCodes.Status200OK, MediaTypeNames.Text.Plain)]
    [ProducesResponseType<string>(StatusCodes.Status503ServiceUnavailable, MediaTypeNames.Text.Plain)]
    public async Task<ContentHttpResult> Ready(
        CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(
            check => check.Tags.Contains("ready"),
            cancellationToken);

        return CreateHealthCheckResponse(report);
    }

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

    private ContentHttpResult CreateHealthCheckResponse(HealthReport report)
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "Thu, 01 Jan 1970 00:00:00 GMT";

        var statusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;

        return TypedResults.Content(
            report.Status.ToString(),
            contentType: "text/plain",
            statusCode: statusCode);
    }
}
