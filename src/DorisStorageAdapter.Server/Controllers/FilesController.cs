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
    IFileService fileService) : BaseController
{
    private readonly IFileService _fileService = fileService;

    [HttpPut("datasets/{identifier}/versions/{version}/files/import")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public async Task<Results<Ok, ForbidHttpResult>> ImportAsync(
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

        await _fileService.ImportAsync(datasetVersion, fromVersion, cancellationToken);

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

        async IAsyncEnumerable<File> ListAsync()
        {
            await foreach (var file in _fileService.ListAsync(datasetVersion, cancellationToken))
            {
                yield return Models.File.FromFileMetadata(file);
            }
        }

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(ListAsync());
    }
}
