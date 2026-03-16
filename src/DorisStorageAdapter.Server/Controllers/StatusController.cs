using DorisStorageAdapter.Server.Controllers.Authorization;
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

public sealed class StatusController(IStatusService statusService) : BaseController
{
    private readonly IStatusService _statusService = statusService;

    [HttpPut("datasets/{identifier}/versions/{version}/status/publish")]
    //[Authorize(Roles = Roles.Service)]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok, ForbidHttpResult>> PublishAsync(
        string identifier,
        string version,
        [FromForm(Name = "access_right")] AccessRight accessRight,
        [FromForm(Name = "canonical_doi")] string canonicalDoi,
        [FromForm] string doi,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        await _statusService.PublishAsync(datasetVersion, accessRight, canonicalDoi, doi, cancellationToken);

        return TypedResults.Ok();
    }

    [HttpPut("datasets/{identifier}/versions/{version}/status")]
    [Authorize(Roles = Roles.Service)]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok, ForbidHttpResult>> SetStatusAsync(
        string identifier, 
        string version,
        [FromForm] DatasetVersionStatus status,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        await _statusService.SetStatusAsync(datasetVersion, status, cancellationToken);

        return TypedResults.Ok();
    }
}
