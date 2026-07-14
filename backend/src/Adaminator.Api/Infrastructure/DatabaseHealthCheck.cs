using Adaminator.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Adaminator.Api.Infrastructure;

/// <summary>Reports the API's ability to reach the PostgreSQL database.</summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly AdaminatorDbContext _dbContext;

    public DatabaseHealthCheck(AdaminatorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy("Database reachable")
            : HealthCheckResult.Unhealthy("Database unreachable");
    }
}
