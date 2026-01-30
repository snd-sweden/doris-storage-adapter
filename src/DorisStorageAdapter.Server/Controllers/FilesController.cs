using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Server.Controllers.Models;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

public sealed class FilesController(
    IFileService fileService,
    IStorageLimitsService limitsService) : BaseController
{
    private readonly IFileService fileService = fileService;
    private readonly IStorageLimitsService limitsService = limitsService;

    [HttpPut("datasets/{identifier}/versions/{version}/files/import")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public async Task<Results<Ok, ForbidHttpResult>> Import(
        string identifier,
        string version,
        [FromQuery, BindRequired] string fromVersion,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        await fileService.Import(datasetVersion, fromVersion, cancellationToken);

        return TypedResults.Ok();
    }

    [HttpGet("datasets/{identifier}/versions/{version}/files")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<IEnumerable<File>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public Results<Ok<IAsyncEnumerable<File>>, ForbidHttpResult> List(
        string identifier,
        string version,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        async IAsyncEnumerable<File> List()
        {
            await foreach (var file in fileService.List(datasetVersion, cancellationToken))
            {
                yield return Models.File.FromFileMetadata(file);
            }
        }

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(List());
    }

    [HttpGet("datasets/{identifier}/versions/{version}/files/limits")]
    public async Task<Results<Ok<StorageLimits>, ForbidHttpResult>> GetStorageLimits(
     string identifier,
     string version,
     CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        var result = await limitsService.GetStorageLimits(datasetVersion, cancellationToken);

        return TypedResults.Ok(result);
    }

    [HttpPut("datasets/{identifier}/versions/{version}/files/limits")]
    public async Task<Results<Ok, ForbidHttpResult>> SetStorageLimits(
        string identifier,
        string version,
        StorageLimits storageLimits,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        await limitsService.SetStorageLimits(datasetVersion, storageLimits, cancellationToken);

        return TypedResults.Ok();
    }
}
