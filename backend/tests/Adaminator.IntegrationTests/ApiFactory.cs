using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
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

    /// <summary>Matches the API's own serializer: camel case with string enums.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>A client carrying an admin bearer token, for the endpoints that require one.</summary>
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = AdminPassword });
        // Fail here rather than leaving every test in the class to fail on an unexplained 401.
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return client;
    }

    private record LoginResponse(string Token);

    /// <summary>
    /// Creates a tournament and returns its id. The defaults are the shape most tests want - a Single
    /// Elimination cup under a name no other test can collide with - and every field the suite actually
    /// varies is a parameter. The create contract is the one that keeps growing, so it is spelled out
    /// here once instead of in an anonymous object per test class, where a newly required field means
    /// four edits and a 400 in whichever one is missed.
    /// </summary>
    public static async Task<Guid> CreateTournamentAsync(
        HttpClient client,
        string type = "SingleElimination",
        bool thirdPlaceEnabled = false,
        string defaultMatchFormat = "Bo3",
        string? groupStageMatchFormat = null,
        int groupCount = 0)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Cup {Guid.NewGuid():N}",
            date = "2026-07-14",
            type,
            defaultMatchFormat,
            groupStageMatchFormat,
            thirdPlaceEnabled,
            groupCount
        });

        // Fail here rather than leaving the caller to explain an empty Guid.
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions))!.Id;
    }

    public record CreatedTournament(Guid Id, string PublicToken);

    // The image goes to the constructor rather than WithImage: Testcontainers 4.14 deprecated the
    // parameterless form, and it was already pinned here so the tests never float onto a new Postgres.
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:16-alpine").Build();

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
        // WebApplicationFactory's in-memory TestServer has no real client IP, so every login in the
        // whole run shares one rate-limit bucket - raise it well above what any test class needs.
        builder.UseSetting("RateLimiting:Login:PermitLimit", "1000");
    }
}
