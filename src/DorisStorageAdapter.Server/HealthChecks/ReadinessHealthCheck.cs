using DorisStorageAdapter.Services.Contract;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Server.HealthChecks;

internal sealed class ReadinessHealthCheck(
    ISystemService systemService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        bool isReady = await systemService.IsReadyAsync(
            cancellationToken);

        return isReady
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Application is not ready");
    }
}
