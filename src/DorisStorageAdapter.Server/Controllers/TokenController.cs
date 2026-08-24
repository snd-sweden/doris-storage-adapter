using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Configuration;
using DorisStorageAdapter.Server.Controllers.Attributes;
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
public sealed class TokenController(
    IJwtService jwtService, 
    IConfiguration configuration,
    TimeProvider timeProvider) : ControllerBase
{
    private readonly IJwtService _jwtService = jwtService;
    private readonly IConfiguration _configuration = configuration;
    private readonly TimeProvider _timeProvider = timeProvider;

    [HttpPost("dev/token/{identifier}/{version}")]
    public async Task<string> CreateTokenAsync(
        string identifier, 
        string version, 
        [FromQuery] string role,
        [FromQuery] string? tenantId,
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
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = jwksUri.Scheme + "://" + jwksUri.Authority,
            Audience = publicUrl.AbsoluteUri,
            Subject = new(claims),
            Expires = _timeProvider.GetUtcNow().AddHours(1).UtcDateTime,
            SigningCredentials = key
        };

        if (tenantId != null)
        {
            tokenDescriptor.Subject.AddClaim(new(Claims.TenantId, tenantId));
        }

        return tokenHandler.CreateToken(tokenDescriptor);
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
