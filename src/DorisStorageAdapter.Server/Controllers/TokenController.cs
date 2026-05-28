using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Attributes;
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
    private readonly IJwtService _jwtService = jwtService;
    private readonly IConfiguration _configuration = configuration;

    [HttpPost("dev/token/{identifier}/{version}")]
    public Task<string> CreateDataAccessTokenAsync(
        string identifier, 
        string version, 
        [FromQuery] string role,
        [FromQuery] string? tenantId)
    {
        return CreateTokenAsync(identifier, version, role, tenantId);
    }

    private async Task<string> CreateTokenAsync(
        string identifier, string version, string role, string? tenantId)
    {
        var key = await _jwtService.GetCurrentSigningCredentials();
        var publicUrl = _configuration.Get<GeneralConfiguration>()!.PublicUrl;
        var jwksUri = _configuration
            .GetSection(SecurityConfiguration.ConfigurationSection)
            .Get<SecurityConfiguration>()!
            .JwksUri;

        var tokenHandler = new JsonWebTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
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
        };

        if (tenantId != null)
        {
            tokenDescriptor.Subject.AddClaim(new(Claims.TenantId, tenantId));
        }

        return tokenHandler.CreateToken(tokenDescriptor);
    }
}
