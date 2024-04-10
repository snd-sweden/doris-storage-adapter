using DatasetFileUpload.Models;
using DatasetFileUpload.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DatasetFileUpload.Controllers;

[ApiController]
[Authorize]
public class AuthController(ILogger<AuthController> logger, IConfiguration configuration) : Controller
{
    private readonly ILogger logger = logger;
    private readonly IConfiguration configuration = configuration;
    private readonly TokenService tokenService = new(configuration);

    [HttpPost("token/{datasetIdentifier}/{versionNumber}")]
    [Authorize(Roles = "UploadService")]
    public string GetUploadToken(string datasetIdentifier, string versionNumber, [FromBody] AuthInfo user)
    {
        return tokenService.GetUploadToken(user, new DatasetVersionIdentifier(datasetIdentifier, versionNumber));
    }
}
