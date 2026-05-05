using Microsoft.AspNetCore.Http;

namespace DorisStorageAdapter.Server.Tenancy;

public interface ITenantResolver
{
    string? ResolveTenantId(HttpContext context);
}
