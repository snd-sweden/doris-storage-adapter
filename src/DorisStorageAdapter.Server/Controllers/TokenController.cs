using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Attributes;
using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Services.Contract.Audit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NetDevPack.Security.Jwt.Core.Interfaces;
using System;
using System.Collections.Generic;
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
    public async Task<string> CreateTokenAsync(
        string identifier, 
        string version, 
        [FromQuery] string role,
        [FromBody] AuditUser? user)
    {
        var key = await _jwtService.GetCurrentSigningCredentials();
        var publicUrl = _configuration.Get<GeneralConfiguration>()!.PublicUrl;
        var jwksUri = _configuration
            .GetSection(SecurityConfiguration.ConfigurationSection)
            .Get<SecurityConfiguration>()!
            .JwksUri;

        List<Claim> claims = [
            new Claim("role", role),
            new Claim(Claims.DatasetIdentifier, identifier),
            new Claim(Claims.DatasetVersion, version)
        ];

        AddUserClaims(claims, user);

        var tokenHandler = new JsonWebTokenHandler();
        return tokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwksUri.Scheme + "://" + jwksUri.Authority,
            Audience = publicUrl.AbsoluteUri,
            Subject = new(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = key
        });
    }

    private static void AddUserClaims(List<Claim> claims, AuditUser? user)
    {
        if (user == null)
        {
            return;
        }

        void AddIfNotNull(string claimName, string? value)
        {
            if (value != null)
            {
                claims.Add(new(claimName, value));
            }
        }

        AddIfNotNull("edu_person_principal_name", user.EduPersonPrincipalName);
        AddIfNotNull("email", user.Email);
        AddIfNotNull("family_name", user.FamilyName);
        AddIfNotNull("given_name", user.GivenName);
        AddIfNotNull("name", user.Name);
        AddIfNotNull("orcid", user.Orcid);
    }
}
