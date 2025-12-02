using DorisStorageAdapter.Helpers;
using DorisStorageAdapter.Server.Controllers.Attributes;
using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Server.Controllers.Dtos;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

[ApiController]
public sealed class FileController(IFileService fileService) : ControllerBase
{
    private readonly IFileService fileService = fileService;

    private const string corsPrefix = nameof(FileController) + "_";

    public const string storeCorsPolicyName = corsPrefix + nameof(Store);
    public const string deleteCorsPolicyName = corsPrefix + nameof(Delete);
    public const string getPublicDataCorsPolicyName = corsPrefix + nameof(GetPublicData);

    [HttpPut("file/{identifier}/{version}/{type}")]
    [Authorize(Roles = Roles.WriteData)]
    [DisableRequestSizeLimit] // Disable request size limit to allow streaming large files
    // DisableFormValueModelBinding makes sure that ASP.NET does not try to parse the body as form data
    // when Content-Type is "multipart/form-data" or "application/x-www-form-urlencoded".
    [DisableFormValueModelBinding]
    [EnableCors(storeCorsPolicyName)]
    [BinaryRequestBody("*/*")]
    [ProducesResponseType<FileDto>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status411LengthRequired, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok<FileDto>, ForbidHttpResult, ProblemHttpResult>> Store(
        string identifier,
        string version,
        FileType type,
        [FromQuery, BindRequired] string filePath,
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

        var result = await fileService.Store(
            datasetVersion: datasetVersion,
            type: type,
            filePath: filePath,
            data: Request.Body,
            size: Request.Headers.ContentLength.Value,
            contentType: Request.Headers.ContentType,
            cancellationToken: cancellationToken);

        return TypedResults.Ok(ToFileDto(result));
    }

    [HttpDelete("file/{identifier}/{version}/{type}")]
    [Authorize(Roles = Roles.WriteData)]
    [EnableCors(deleteCorsPolicyName)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, MediaTypeNames.Application.ProblemJson)]
    public async Task<Results<Ok, ForbidHttpResult>> Delete(
        string identifier,
        string version,
        FileType type,
        [FromQuery, BindRequired] string filePath,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        await fileService.Delete(datasetVersion, type, filePath, cancellationToken);

        return TypedResults.Ok();
    }

    [HttpPut("file/{identifier}/{version}/import")]
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

    [HttpHead("file/{identifier}/{version}/{type}")]
    [HttpGet("file/{identifier}/{version}/{type}")]
    [Authorize(Roles = Roles.ReadData)]
    [SwaggerResponse(StatusCodes.Status200OK, null, typeof(FileStreamResult), "*/*")]
    [SwaggerResponse(StatusCodes.Status206PartialContent, null, typeof(FileStreamResult), "*/*")]
    [ProducesResponseType(typeof(void), StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    public async Task<Results<FileStreamHttpResult, ForbidHttpResult, NotFound>> GetData(
        string identifier,
        string version,
        FileType type,
        [FromQuery, BindRequired] string filePath,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        var result = await GetData(datasetVersion, type, filePath, false, cancellationToken);

        if (result == null)
        {
            return TypedResults.NotFound();
        }

        return result;
    }

    [HttpHead("file/public/{identifier}/{version}/{type}")]
    [HttpGet("file/public/{identifier}/{version}/{type}")]
    [EnableCors(getPublicDataCorsPolicyName)]
    [SwaggerResponse(StatusCodes.Status200OK, null, typeof(FileStreamResult), "*/*")]
    [SwaggerResponse(StatusCodes.Status206PartialContent, null, typeof(FileStreamResult), "*/*")]
    [ProducesResponseType(typeof(void), StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    public async Task<Results<FileStreamHttpResult, NotFound>> GetPublicData(
       string identifier,
       string version,
       FileType type,
       [FromQuery, BindRequired] string filePath,
       CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        var result = await GetData(datasetVersion, type, filePath, true, cancellationToken);

        if (result == null)
        {
            return TypedResults.NotFound();
        }

        return result;
    }

    [HttpGet("file/{identifier}/{version}/zip")]
    [Authorize(Roles = Roles.ReadData)]
    [SwaggerResponse(StatusCodes.Status200OK, null, typeof(FileStreamResult), MediaTypeNames.Application.Zip)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public Results<PushStreamHttpResult, ForbidHttpResult> GetDataAsZip(
        string identifier,
        string version,
        [FromQuery] string[] path,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Stream(_ =>
            fileService.WriteDataAsZip(
                datasetVersion,
                path,
                Response.BodyWriter.AsStream(),
                cancellationToken),
            MediaTypeNames.Application.Zip,
            identifier + '-' + version + ".zip");
    }

    [HttpGet("file/{identifier}/{version}")]
    [Authorize(Roles = Roles.Service)]
    [ProducesResponseType<IEnumerable<FileDto>>(StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public Results<Ok<IAsyncEnumerable<FileDto>>, ForbidHttpResult> List(
        string identifier,
        string version,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        async IAsyncEnumerable<FileDto> List()
        {
            await foreach (var file in fileService.List(datasetVersion, cancellationToken))
            {
                yield return ToFileDto(file);
            }
        }

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(List());
    }

    private bool CheckClaims(DatasetVersion datasetVersion) =>
        Claims.CheckClaims(datasetVersion, User.Claims);

    private static FileDto ToFileDto(FileMetadata file) => new(
        ContentSize: file.Size,
        DateCreated: file.DateCreated,
        DateModified: file.DateModified,
        EncodingFormat: file.ContentType,
        Name: file.Path,
#pragma warning disable CA1308 // Normalize strings to uppercase
        Sha256: file.Sha256 == null ? null : Convert.ToHexString(file.Sha256).ToLowerInvariant(),
#pragma warning restore CA1308
        Type: file.Type
    );

    private async Task<FileStreamHttpResult?> GetData(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        bool restrictToPubliclyAccessible,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        ByteRange? ParseByteRange()
        {
            var rangeHeader = Request.GetTypedHeaders().Range;
            if (rangeHeader != null && rangeHeader.Ranges.Count == 1)
            {
                var rangeItem = rangeHeader.Ranges.First();
                return new(rangeItem.From, rangeItem.To);
            }

            return null;
        }

        var data = await fileService.GetData(
            datasetVersion: datasetVersion,
            type: type,
            filePath: filePath,
            isHeadRequest: Request.Method == HttpMethods.Head,
            byteRange: ParseByteRange(),
            restrictToPubliclyAccessible: restrictToPubliclyAccessible,
            cancellationToken: cancellationToken);

        if (data == null)
        {
            return null;
        }

        // Use a fake seekable stream here in order for TypedResults.Stream()
        // to work as intended when using byte ranges.
        // fileData.Stream as returned from fileService.GetFileData() is already sliced
        // according to the given byte range, but the internal logic in TypedResults.Stream()
        // will try to seek according to the byte range. Using a FakeSeekableStream fixes
        // that by making seeking a no-op.
        data = data with
        {
            Stream = new FakeSeekableStream(data.Stream, data.Size)
        };

        return TypedResults.Stream(
            stream: data.Stream,
            contentType: data.ContentType,
            fileDownloadName: filePath.Split('/').Last(),
            enableRangeProcessing: true);
    }
}
