using DorisStorageAdapter.Server.Authorization;
using DorisStorageAdapter.Server.Controllers.Models.Responses;
using DorisStorageAdapter.Server.Tenancy;
using DorisStorageAdapter.Services.Contract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.Controllers;

public class DeduplicationController(
    IDeduplicationService deduplicationService,
    ITenantResolver tenantResolver) : BaseController(tenantResolver)
{
    private readonly IDeduplicationService _deduplicationService = deduplicationService;

    [HttpPut("datasets/deduplicate")]
    //[Authorize(Roles = Roles.Service)]
    [ProducesResponseType<ErrorProblemDetails>(StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson)]
    [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(void), StatusCodes.Status403Forbidden)]
    public async Task DeduplicateAsync(
       CancellationToken cancellationToken)
    {
        await _deduplicationService.DeduplicateAsync(cancellationToken);
    }
}

