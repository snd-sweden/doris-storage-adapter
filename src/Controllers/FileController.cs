using DatasetFileUpload.Authorization;
using DatasetFileUpload.Models;
using DatasetFileUpload.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace DatasetFileUpload.Controllers;

[ApiController]
public class FileController(ILogger<FileController> logger, FileService fileService) : Controller
{
    private readonly ILogger logger = logger;
    private readonly FileService fileService = fileService;

    [HttpPut("file/{datasetIdentifier}/{versionNumber}")]
    [Authorize(Roles = Roles.Service)]
    public async Task<Results<Ok, ProblemHttpResult>> SetupVersion(string datasetIdentifier, string versionNumber)
    {
        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        await fileService.SetupVersion(datasetVersion);

        return TypedResults.Ok();
    }

    [HttpPut("{datasetIdentifier}/{versionNumber}/publish")]
    [Authorize(Roles = Roles.Service)]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<Results<Ok, ProblemHttpResult>> PublishVersion(
        string datasetIdentifier,
        string versionNumber,
        [FromForm] AccessRightEnum access_right,
        [FromForm] string doi)
    {
        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        await fileService.PublishVersion(datasetVersion, access_right, doi);

        return TypedResults.Ok();
    }

    [HttpPut("{datasetIdentifier}/{versionNumber}/withdraw")]
    [Authorize(Roles = Roles.Service)]
    public async Task<Results<Ok, ProblemHttpResult>> WithdrawVersion(string datasetIdentifier, string versionNumber)
    {
        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        await fileService.WithdrawVersion(datasetVersion);

        return TypedResults.Ok();
    }

    // A possible drawback of not using POST with multipart/form-data is that 
    // using PUT requires CORS.
    [HttpPut("file/{datasetIdentifier}/{versionNumber}/{type}")]
    [Authorize(Roles = Roles.WriteData)]
    // Disable request size limit to allow streaming large files
    [DisableRequestSizeLimit]
    public async Task<Results<Ok<RoCrateFile>, ForbidHttpResult, ProblemHttpResult>> Upload(
        string datasetIdentifier,
        string versionNumber,
        FileTypeEnum type,
        [FromQuery] string filePath)
    {
        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        if (!CheckDatasetVersionClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        if (Request.Headers.ContentLength == null)
        {
            return TypedResults.Problem("Missing Content-Length.", statusCode: 400);
        }

        logger.LogDebug("Upload datasetIdentifier: {datasetIdentifier}, versionNumber: {versionNumber}:, type: {type}, filePath: {filePath}",
                  datasetIdentifier, versionNumber, type, filePath);

        var result = await fileService.Upload(datasetVersion, type, filePath, new(Request.Body, Request.Headers.ContentLength.Value));
        return TypedResults.Ok(result);
    }


    [HttpDelete("file/{datasetIdentifier}/{versionNumber}/{type}")]
    [Authorize(Roles = Roles.WriteData)]
    public async Task<Results<Ok, ForbidHttpResult, ProblemHttpResult>> Delete(string datasetIdentifier, string versionNumber, FileTypeEnum type, string filePath)
    {
        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        if (!CheckDatasetVersionClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        logger.LogDebug("Delete datasetIdentifier: {datasetIdentifier}, versionNumber: {versionNumber}:, type: {type}, filePath: {filePath}",
            datasetIdentifier, versionNumber, type, filePath);

        await fileService.Delete(datasetVersion, type, filePath);

        return TypedResults.Ok();
    }

    [HttpGet("/file/{datasetIdentifier}/{versionNumber}/{type}")]
    [Authorize(Roles = Roles.ReadData)]
    public async Task<Results<FileStreamHttpResult, ForbidHttpResult, NotFound, ProblemHttpResult>> GetData(
        string datasetIdentifier,
        string versionNumber,
        FileTypeEnum type,
        string filePath)
    {
        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        if (!CheckDatasetVersionClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        logger.LogDebug("GetData datasetIdentifier: {datasetIdentifier}, versionNumber: {versionNumber}, type: {type}, filePath: {filePath}",
            datasetIdentifier, versionNumber, type, filePath);

        var fileData = await fileService.GetData(datasetVersion, type, filePath);

        if (fileData == null)
        {
            return TypedResults.NotFound();
        }

        Response.Headers.ContentLength = fileData.Length;

        return TypedResults.Stream(fileData.Stream, "application/octet-stream", filePath);
    }

    [HttpGet("/file/{datasetIdentifier}/{versionNumber}/zip")]
    [Authorize(Roles = Roles.ReadData)]
    public async Task<Results<Ok, ForbidHttpResult>> GetDataAsZip(
        string datasetIdentifier,
        string versionNumber,
        [FromQuery] string[] path)
    {
        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        if (!CheckDatasetVersionClaims(datasetVersion))
        {
            return TypedResults.Forbid();
        }

        logger.LogDebug("GetDataAsZip datasetIdentifier: {datasetIdentifier}, versionNumber: {versionNumber}, path {path}",
            datasetIdentifier, versionNumber, path);

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = "attachment; filename=" + datasetIdentifier + "-" + versionNumber + ".zip";

        using var archive = new ZipArchive(Response.BodyWriter.AsStream(), ZipArchiveMode.Create, false);

        await foreach (var (type, filePath, data) in fileService.GetDataByPaths(datasetVersion, path))
        {
            var entry = archive.CreateEntry(type.ToString().ToLower() + '/' + filePath, CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            await data.Stream.CopyToAsync(entryStream);
        }

        return TypedResults.Ok();

        /*await foreach (var file in fileService.ListFiles(datasetVersion))
        {
            string filePath = file.AdditionalType.ToString().ToLowerInvariant() + '/' + file.Id;

            if (path.Length > 0 && !path.Any(filePath.StartsWith))
            {
                continue;
            }

            var fileData = await fileService.GetData(datasetVersion, file.AdditionalType, file.Id);

            if (fileData != null)
            {
                var entry = archive.CreateEntry(filePath, CompressionLevel.NoCompression);
                using var entryStream = entry.Open();
                await fileData.Stream.CopyToAsync(entryStream);
            }
        }*/
    }

    [HttpGet("/file/{datasetIdentifier}/{versionNumber}")]
    [Authorize(Roles = Roles.Service)]
    public async IAsyncEnumerable<RoCrateFile> ListFiles(string datasetIdentifier, string versionNumber)
    {
        logger.LogDebug("ListFiles datasetIdentifier: {datasetIdentifier}, versionNumber: {versionNumber}",
            datasetIdentifier, versionNumber);

        var datasetVersion = new DatasetVersionIdentifier(datasetIdentifier, versionNumber);

        await foreach (var file in fileService.ListFiles(datasetVersion))
        {
            yield return file;
        }
    }

    private bool CheckDatasetVersionClaims(DatasetVersionIdentifier datasetVersion) =>
        User.Claims.Any(c => c.Type == Claims.DatasetIdentifier && c.Value == datasetVersion.DatasetIdentifier) &&
        User.Claims.Any(c => c.Type == Claims.DatasetVersionNumber && c.Value == datasetVersion.VersionNumber);

    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("/error")]
    public IActionResult HandleError() =>
        Problem();
}
