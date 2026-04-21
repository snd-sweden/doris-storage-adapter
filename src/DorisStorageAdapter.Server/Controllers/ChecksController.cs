using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
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

public sealed class ChecksController(ICheckService checkService) : BaseController
{
    private readonly ICheckService _checkService = checkService;

    [HttpGet("datasets/{identifier}/versions/{version}/checks/consistency")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<ConsistencyCheckResult>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok<ConsistencyCheckResult>, BadRequest, ForbidHttpResult>> CheckConsistencyAsync(
       string identifier,
       string version,
       CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        var errors = await _checkService.CheckConsistencyAsync(
            datasetVersion: datasetVersion,
            cancellationToken: cancellationToken);

        return TypedResults.Ok(new ConsistencyCheckResult(errors));
    }
}
