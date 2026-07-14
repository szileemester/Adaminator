using Adaminator.Application.Tournaments;
using Adaminator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Adaminator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AdaminatorDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ITournamentRepository, TournamentRepository>();
        return services;
    }
}
