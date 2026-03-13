using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Attributes;
using DorisStorageAdapter.Server.Controllers.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NetDevPack.Security.Jwt.Core.Interfaces;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

[DevOnly]
[ApiExplorerSettings(IgnoreApi = true)]
[ApiController]
public sealed class TokenController(IJwtService jwtService, IConfiguration configuration) : ControllerBase
{
    private readonly IJwtService jwtService = jwtService;
    private readonly IConfiguration configuration = configuration;

    [HttpPost("dev/token/{identifier}/{version}")]
    public Task<string> CreateDataAccessTokenAsync(string identifier, string version, [FromQuery] string role)
    {
        return CreateTokenAsync(identifier, version, role);
    }

    private async Task<string> CreateTokenAsync(string identifier, string version, string role)
    {
        var key = await jwtService.GetCurrentSigningCredentials();
        var publicUrl = configuration.Get<GeneralConfiguration>()!.PublicUrl;
        var jwksUri = configuration
            .GetSection(AuthorizationConfiguration.ConfigurationSection)
            .Get<AuthorizationConfiguration>()!
            .JwksUri;

        var tokenHandler = new JsonWebTokenHandler();
        return tokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwksUri.Scheme + "://" + jwksUri.Authority,
            Audience = publicUrl.AbsoluteUri,
            Subject = new([
                    new Claim("role", role),
                    new Claim(Claims.DatasetIdentifier, identifier),
                    new Claim(Claims.DatasetVersion, version)
                 ]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = key
        });
    }
}
