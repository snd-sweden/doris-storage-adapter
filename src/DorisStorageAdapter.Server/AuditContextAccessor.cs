using DorisStorageAdapter.Server.Controllers.Authorization;
using DorisStorageAdapter.Services.Contract.Audit;
using Microsoft.AspNetCore.Http;

namespace DorisStorageAdapter.Server;

internal sealed class AuditContextAccessor(
    IHttpContextAccessor httpContextAccessor) : IAuditContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public AuditContext Current
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;

            string? GetUserClaim(string claimName) =>
              context?.User.FindFirst(claimName)?.Value;

            return new()
            {
                InitiatorType = context?.User.IsInRole(Roles.Service) == false
                    ? AuditInitiatorType.User : AuditInitiatorType.Service,

                IPAddress = context?.Connection.RemoteIpAddress,
                TraceId = context?.TraceIdentifier,

                User = new()
                {
                    EduPersonPrincipalName = GetUserClaim("edu_person_principal_name"),
                    Email = GetUserClaim("email"),
                    FamilyName = GetUserClaim("family_name"),
                    GivenName = GetUserClaim("given_name"),
                    Name = GetUserClaim("name"),
                    Orcid = GetUserClaim("orcid")
                }
            };
        }
    }
}
