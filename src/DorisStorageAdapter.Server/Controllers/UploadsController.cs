using DorisStorageAdapter.Server.Controllers.Attributes;
using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

public sealed class UploadsController(
    IFileService fileService) : BaseController
{
    private readonly IFileService fileService = fileService;

    private const string CorsPrefix = nameof(UploadsController) + "_";
    public const string UploadCorsPolicyName = CorsPrefix + nameof(UploadAsync);
    public const string DeleteCorsPolicyName = CorsPrefix + nameof(DeleteAsync);

    [HttpPut("uploads/{identifier}/{version}/{**filePath}")]
    [Authorize(Roles = Roles.WriteDraftFiles)]
    [DisableRequestSizeLimit] // Disable request size limit to allow streaming large files
    // DisableFormValueModelBinding makes sure that ASP.NET does not try to parse the body as form data
    // when Content-Type is "multipart/form-data" or "application/x-www-form-urlencoded".
    [DisableFormValueModelBinding]
    [EnableCors(UploadCorsPolicyName)]
    [BinaryRequestBody("*/*")]
    [ProducesResponseType<File>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status411LengthRequired, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok<File>, ForbidHttpResult, ProblemHttpResult>> UploadAsync(
        string identifier,
        string version,
        string filePath,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        if (Request.Headers.ContentLength == null)
        {
            return TypedResults.Problem("Missing Content-Length.", statusCode: 411);
        }

        var result = await fileService.StoreAsync(
            datasetVersion: datasetVersion,
            filePath: filePath,
            data: Request.Body,
            size: Request.Headers.ContentLength.Value,
            cancellationToken: cancellationToken);

        return TypedResults.Ok(Models.Responses.File.FromFileMetadata(result));
    }

    [HttpDelete("uploads/{identifier}/{version}/{**filePath}")]
    [Authorize(Roles = Roles.WriteDraftFiles)]
    [EnableCors(DeleteCorsPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok, ForbidHttpResult>> DeleteAsync(
        string identifier,
        string version,
        string filePath,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        await fileService.DeleteAsync(
            datasetVersion: datasetVersion, 
            filePath: filePath, 
            cancellationToken: cancellationToken);

        return TypedResults.Ok();
    }
}
