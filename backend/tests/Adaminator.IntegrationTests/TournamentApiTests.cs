using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace Adaminator.IntegrationTests;

public class TournamentApiTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public TournamentApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task Creating_a_tournament_without_a_token_is_unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "No Auth Cup",
            date = "2026-07-14",
            type = "SingleElimination",
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_create_then_read_a_tournament()
    {
        var client = await CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Summer Cup",
            date = "2026-07-14",
            notes = "test",
            type = "SingleElimination",
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = true
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<TournamentResponse>(JsonOptions);
        created.Should().NotBeNull();
        created!.Status.Should().Be("Planned");
        created.ThirdPlaceEnabled.Should().BeTrue();
        created.PublicToken.Should().NotBeNullOrWhiteSpace();

        var fetch = await client.GetAsync($"/api/tournaments/{created.Id}");
        fetch.StatusCode.Should().Be(HttpStatusCode.OK);

        // The public, unauthenticated view resolves by token.
        var publicClient = _factory.CreateClient();
        var publicView = await publicClient.GetAsync($"/api/public/tournaments/{created.PublicToken}");
        publicView.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Double_elimination_with_third_place_is_rejected()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Invalid Cup",
            date = "2026-07-14",
            type = "DoubleElimination",
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ApiFactory.AdminPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await login.Content.ReadFromJsonAsync<LoginResponseBody>(JsonOptions);
        token.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return client;
    }

    private record TournamentResponse(Guid Id, string Status, bool ThirdPlaceEnabled, string PublicToken);

    private record LoginResponseBody(string Token, DateTimeOffset ExpiresAt);
}
