using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Adaminator.Infrastructure.Persistence;

/// <summary>
/// Enables the EF Core CLI (migrations) to instantiate the context at design time
/// without booting the API. The connection string here is only used to pick the
/// Npgsql provider; migrations do not connect to a live database.
/// </summary>
public class AdaminatorDbContextFactory : IDesignTimeDbContextFactory<AdaminatorDbContext>
{
    public AdaminatorDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AdaminatorDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=adaminator;Username=postgres;Password=postgres")
            .Options;

        return new AdaminatorDbContext(options);
    }
}
