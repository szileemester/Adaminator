using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Adaminator.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container so integration tests exercise
/// the full HTTP + EF Core + database stack. Requires a running Docker engine.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminPassword = "test-password";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync() => await _database.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _database.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _database.GetConnectionString());
        builder.UseSetting("Admin:Password", AdminPassword);
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-that-is-sufficiently-long-1234567890");
    }
}
