using DorisStorageAdapter.Common;
using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Attributes;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Server.Tenancy;
using DorisStorageAdapter.Services.Contract;
using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

public sealed class DownloadsController(
    IFileService fileService,
    IOptions<SecurityConfiguration> securityConfiguration,
    ITenantResolver tenantResolver) : BaseController(tenantResolver)
{
    private readonly IFileService _fileService = fileService;
    private readonly SecurityConfiguration _securityConfiguration = securityConfiguration.Value;

    private const string CorsPrefix = nameof(DownloadsController) + "_";
    public const string DownloadPublicFileCorsPolicyName = CorsPrefix + nameof(DownloadPublicFileAsync);
    public const string DownloadPublicFilesAsZipCorsPolicyName = CorsPrefix + nameof(DownloadPublicFilesAsZip);

    [HttpHead("downloads/draft/{identifier}/{version}/{**filePath}")]
    [HttpGet("downloads/draft/{identifier}/{version}/{**filePath}")]
    [Authorize(Roles = Roles.ReadDraftFiles)]
    [BinaryResponseBody(StatusCodes.Status200OK, "*/*")]
    [BinaryResponseBody(StatusCodes.Status206PartialContent, "*/*")]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(void), StatusCodes.Status416RangeNotSatisfiable)]
    public async Task<Results<FileStreamHttpResult, ForbidHttpResult, NotFound>> DownloadDraftFileAsync(
        string identifier,
        string version,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!_securityConfiguration.AllowReadDraftFiles)
        {
            return TypedResults.Forbid();
        }

        var datasetVersion = CreateDatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        var result = await GetDataAsync(
            datasetVersion: datasetVersion,
            filePath: filePath,
            scope: FileAccessScope.Draft,
            cancellationToken: cancellationToken);

        if (result == null)
        {
            return TypedResults.NotFound();
        }

        return result;
    }

    [HttpHead("downloads/public/{identifier}/{version}/{**filePath}")]
    [HttpGet("downloads/public/{identifier}/{version}/{**filePath}")]
    [EnableCors(DownloadPublicFileCorsPolicyName)]
    [BinaryResponseBody(StatusCodes.Status200OK, "*/*")]
    [BinaryResponseBody(StatusCodes.Status206PartialContent, "*/*")]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(void), StatusCodes.Status416RangeNotSatisfiable)]
    public async Task<Results<FileStreamHttpResult, NotFound>> DownloadPublicFileAsync(
       string identifier,
       string version,
       string filePath,
       CancellationToken cancellationToken)
    {
        var datasetVersion = CreateDatasetVersion(identifier, version);

        var result = await GetDataAsync(
            datasetVersion: datasetVersion,
            filePath: filePath,
            scope: FileAccessScope.Public,
            cancellationToken: cancellationToken);

        if (result == null)
        {
            return TypedResults.NotFound();
        }

        return result;
    }

    [HttpGet("downloads/draft/{identifier}/{version}.zip")]
    [Authorize(Roles = Roles.ReadDraftFiles)]
    [BinaryResponseBody(StatusCodes.Status200OK, MediaTypeNames.Application.Zip)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    public async Task<IResult> DownloadDraftFilesAsZip(
        string identifier,
        string version,
        [FromQuery] string[] path,
        CancellationToken cancellationToken)
    {
        if (!_securityConfiguration.AllowReadDraftFiles)
        {
            return TypedResults.Forbid();
        }

        var datasetVersion = CreateDatasetVersion(identifier, version);

        if (!CheckClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        return await TryWriteDataAsZip(
            datasetVersion: datasetVersion,
            paths: path,
            scope: FileAccessScope.Draft,
            cancellationToken: cancellationToken);
    }

    [HttpGet("downloads/public/{identifier}/{version}.zip")]
    [EnableCors(DownloadPublicFilesAsZipCorsPolicyName)]
    [BinaryResponseBody(StatusCodes.Status200OK, MediaTypeNames.Application.Zip)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status404NotFound)]
    public Task<IResult> DownloadPublicFilesAsZip(
        string identifier,
        string version,
        [FromQuery] string[] path,
        CancellationToken cancellationToken)
    {
        var datasetVersion = CreateDatasetVersion(identifier, version);

        return TryWriteDataAsZip(
            datasetVersion: datasetVersion,
            paths: path,
            scope: FileAccessScope.Public,
            cancellationToken: cancellationToken);
    }

    private sealed record ResponseStream(
        Stream Stream,
        string? ContentType,
        long Size);

    private async Task<FileStreamHttpResult?> GetDataAsync(
        DatasetVersion datasetVersion,
        string filePath,
        FileAccessScope scope,
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

        ResponseStream response;

        if (HttpMethods.IsHead(Request.Method))
        {
            var metadata = await _fileService.GetMetaDataAsync(
                datasetVersion: datasetVersion,
                filePath: filePath,
                scope: scope,
                cancellationToken: cancellationToken);

            if (metadata is null)
            {
                return null;
            }

            response = new(
                Stream.Null,
                metadata.ContentType,
                metadata.Size);
        }
        else
        {
            var data = await _fileService.GetDataAsync(
                datasetVersion: datasetVersion,
                filePath: filePath,
                scope: scope,
                byteRange: ParseByteRange(),
                cancellationToken: cancellationToken);

            if (data == null)
            {
                return null;
            }

            response = new(
                data.Stream,
                data.ContentType,
                data.Size);
        }

        // Use a fake seekable stream here in order for TypedResults.Stream()
        // to work as intended when using byte ranges.
        // fileData.Stream as returned from fileService.GetFileDataAsync() is already sliced
        // according to the given byte range, but the internal logic in TypedResults.Stream()
        // will try to seek according to the byte range. Using a FakeSeekableStream fixes
        // that by making seeking a no-op.
        response = response with
        {
            Stream = new FakeSeekableStream(response.Stream, response.Size)
        };

        return TypedResults.Stream(
            stream: response.Stream,
            contentType: response.ContentType,
            fileDownloadName: filePath.Split('/').Last(),
            enableRangeProcessing: true);
    }

    private async Task<IResult> TryWriteDataAsZip(
        DatasetVersion datasetVersion,
        string[] paths,
        FileAccessScope scope,
        CancellationToken cancellationToken)
    {
        var contentDisposition = new ContentDispositionHeaderValue("attachment");
        contentDisposition.SetHttpFileName(
            datasetVersion.Identifier + '-' + datasetVersion.Version + ".zip");
        Response.Headers.ContentDisposition = contentDisposition.ToString();

        Response.Headers.ContentType = MediaTypeNames.Application.Zip;
        Response.StatusCode = StatusCodes.Status200OK;

        bool success = await _fileService.TryWriteDataAsZipAsync(
            datasetVersion: datasetVersion,
            paths: paths,
            stream: Response.BodyWriter.AsStream(),
            scope: scope,
            cancellationToken: cancellationToken);

        if (!success)
        {
            if (!Response.HasStarted)
            {
                Response.Headers.Clear();
                return TypedResults.NotFound();
            }
        }

        return Results.Empty;
    }
}
