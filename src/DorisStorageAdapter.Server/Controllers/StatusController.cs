using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Controllers.Models.Requests;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Server.Tenancy;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

public sealed class StatusController(
    IStatusService statusService,
    ITenantResolver tenantResolver) : BaseController(tenantResolver)
{
    private readonly IStatusService _statusService = statusService;

    [HttpPut("datasets/{identifier}/versions/{version}/status/publish")]
    [Authorize(Roles = Roles.Service)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok, BadRequest, ForbidHttpResult>> PublishAsync(
        string identifier,
        string version,
        [FromBody] PublishRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return TypedResults.BadRequest();
        }

        var datasetVersion = CreateDatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        await _statusService.PublishAsync(
            datasetVersion: datasetVersion,
            accessRight: request.AccessRight,
            canonicalDoi: request.CanonicalDoi,
            doi: request.Doi,
            cancellationToken: cancellationToken);

        return TypedResults.Ok();
    }

    [HttpPut("datasets/{identifier}/versions/{version}/status")]
    [Authorize(Roles = Roles.Service)]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok, BadRequest, ForbidHttpResult>> SetStatusAsync(
        string identifier, 
        string version,
        [FromBody] SetStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            return TypedResults.BadRequest();
        }

        var datasetVersion = CreateDatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        await _statusService.SetStatusAsync(
            datasetVersion: datasetVersion, 
            status: request.Status, 
            cancellationToken: cancellationToken);

        return TypedResults.Ok();
    }
}
