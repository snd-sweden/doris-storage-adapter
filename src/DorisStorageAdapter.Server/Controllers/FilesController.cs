using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Models.Requests;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Server.Tenancy;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

public sealed class FilesController(
    IFileService fileService,
    ITenantResolver tenantResolver) : BaseController(tenantResolver)
{
    private readonly IFileService _fileService = fileService;

    [HttpPut("datasets/{identifier}/versions/{version}/files/import")]
    [Authorize(Roles = Roles.Service)]
    [Consumes("application/json")]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok, BadRequest, ForbidHttpResult>> ImportAsync(
        string identifier,
        string version,
        [FromBody] ImportFilesRequest request,
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

        await _fileService.ImportAsync(
            datasetVersion: datasetVersion, 
            fromVersion: request.FromVersion, 
            cancellationToken: cancellationToken);

        return TypedResults.Ok();
    }

    [HttpGet("datasets/{identifier}/versions/{version}/files")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<IEnumerable<File>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public Results<Ok<IAsyncEnumerable<File>>, ForbidHttpResult> List(
        string identifier,
        string version,
        CancellationToken cancellationToken)
    {
        var datasetVersion = CreateDatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        var result = _fileService
            .ListAsync(datasetVersion, cancellationToken)
            .Select(Models.Responses.File.FromFileMetadata);

        return TypedResults.Ok(result);
    }
}
