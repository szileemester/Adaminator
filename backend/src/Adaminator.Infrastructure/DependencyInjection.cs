using Adaminator.Application.Tournaments;
using Adaminator.Application.Unmatched;
using Adaminator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Adaminator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Retry transient failures: the database is a Fly machine that restarts for its own deploys and
        // maintenance, and without this a dropped socket surfaces as a 500 mid-tournament. Nothing here
        // opens an explicit transaction, which is the one thing an execution strategy cannot retry.
        services.AddDbContext<AdaminatorDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        services.AddScoped<IUnmatchedRepository, UnmatchedRepository>();
        return services;
    }
}
