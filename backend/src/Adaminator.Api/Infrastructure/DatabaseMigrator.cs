using Adaminator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Adaminator.Api.Infrastructure;

public static class DatabaseMigrator
{
    /// <summary>
    /// Applies pending EF Core migrations, retrying to tolerate the database container
    /// still starting up (docker compose ordering).
    /// </summary>
    public static async Task MigrateAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AdaminatorDbContext>();
        var logger = app.Services.GetRequiredService<ILogger<AdaminatorDbContext>>();

        const int maxAttempts = 10;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Database migrations applied.");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}); retrying...", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
