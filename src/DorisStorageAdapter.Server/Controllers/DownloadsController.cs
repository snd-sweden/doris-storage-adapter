using DorisStorageAdapter.Common;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;
using System;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

public sealed class DownloadsController(
    IFileService fileService,
    IOptions<AuthorizationConfiguration> authorizationConfiguration) : BaseController
{
    private readonly IFileService _fileService = fileService;
    private readonly AuthorizationConfiguration _authorizationConfiguration = authorizationConfiguration.Value;

    private const string CorsPrefix = nameof(DownloadsController) + "_";
    public const string DownloadPublicFileCorsPolicyName = CorsPrefix + nameof(DownloadPublicFileAsync);
    public const string DownloadPublicFilesAsZipCorsPolicyName = CorsPrefix + nameof(DownloadPublicFilesAsZip);

    [HttpHead("downloads/draft/{identifier}/{version}/{type}/{**filePath}")]
    [HttpGet("downloads/draft/{identifier}/{version}/{type}/{**filePath}")]
    [Authorize(Roles = Roles.ReadDraftFiles)]
    [SwaggerResponse(StatusCodes.Status200OK, null, typeof(FileStreamResult), "*/*")]
    [SwaggerResponse(StatusCodes.Status206PartialContent, null, typeof(FileStreamResult), "*/*")]
    [ProducesResponseType(typeof(void), StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    public async Task<Results<FileStreamHttpResult, ForbidHttpResult, NotFound>> DownloadDraftFileAsync(
        string identifier,
        string version,
        FileType type,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!_authorizationConfiguration.AllowReadDraftFiles)
        {
            return TypedResults.Forbid();
        }

        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        var result = await GetDataAsync(datasetVersion, type, filePath, true, cancellationToken);

        if (result == null)
        {
            return TypedResults.NotFound();
        }

        return result;
    }

    [HttpHead("downloads/public/{identifier}/{version}/{type}/{**filePath}")]
    [HttpGet("downloads/public/{identifier}/{version}/{type}/{**filePath}")]
    [EnableCors(DownloadPublicFileCorsPolicyName)]
    [SwaggerResponse(StatusCodes.Status200OK, null, typeof(FileStreamResult), "*/*")]
    [SwaggerResponse(StatusCodes.Status206PartialContent, null, typeof(FileStreamResult), "*/*")]
    [ProducesResponseType(typeof(void), StatusCodes.Status416RangeNotSatisfiable)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    public async Task<Results<FileStreamHttpResult, NotFound>> DownloadPublicFileAsync(
       string identifier,
       string version,
       FileType type,
       string filePath,
       CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        var result = await GetDataAsync(datasetVersion, type, filePath, false, cancellationToken);

        if (result == null)
        {
            return TypedResults.NotFound();
        }

        return result;
    }

    [HttpGet("downloads/draft/{identifier}/{version}.zip")]
    [Authorize(Roles = Roles.ReadDraftFiles)]
    [SwaggerResponse(StatusCodes.Status200OK, null, typeof(FileStreamResult), MediaTypeNames.Application.Zip)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public Results<PushStreamHttpResult, ForbidHttpResult> DownloadDraftFilesAsZip(
        string identifier,
        string version,
        [FromQuery] string[] path,
        CancellationToken cancellationToken)
    {
        if (!_authorizationConfiguration.AllowReadDraftFiles)
        {
            return TypedResults.Forbid();
        }

        var datasetVersion = new DatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        return WriteDataAsZip(datasetVersion, path, true, cancellationToken);
    }

    [HttpGet("downloads/public/{identifier}/{version}.zip")]
    [EnableCors(DownloadPublicFilesAsZipCorsPolicyName)]
    [SwaggerResponse(StatusCodes.Status200OK, null, typeof(FileStreamResult), MediaTypeNames.Application.Zip)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    public Results<PushStreamHttpResult, ForbidHttpResult> DownloadPublicFilesAsZip(
        string identifier,
        string version,
        [FromQuery] string[] path,
        CancellationToken cancellationToken)
    {
        var datasetVersion = new DatasetVersion(identifier, version);

        return WriteDataAsZip(datasetVersion, path, false, cancellationToken);
    }

    private async Task<FileStreamHttpResult?> GetDataAsync(
        DatasetVersion datasetVersion,
        FileType type,
        string filePath,
        bool allowDraft,
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

        var data = await _fileService.GetDataAsync(
            datasetVersion: datasetVersion,
            type: type,
            filePath: filePath,
            isHeadRequest: Request.Method == HttpMethods.Head,
            byteRange: ParseByteRange(),
            allowDraft: allowDraft,
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

    private PushStreamHttpResult WriteDataAsZip(
        DatasetVersion datasetVersion,
        string[] paths,
        bool allowDraft,
        CancellationToken cancellationToken)
    {
        return TypedResults.Stream(_ =>
           _fileService.WriteDataAsZipAsync(
               datasetVersion: datasetVersion,
               paths: paths,
               stream: Response.BodyWriter.AsStream(),
               allowDraft: allowDraft,
               cancellationToken: cancellationToken),
           MediaTypeNames.Application.Zip,
           datasetVersion.Identifier + '-' + datasetVersion.Version + ".zip");
    }
}
