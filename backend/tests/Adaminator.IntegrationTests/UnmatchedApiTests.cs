using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace Adaminator.IntegrationTests;

/// <summary>
/// The house Unmatched ladder. The split that matters here is the access one: reading is anonymous so
/// the page can be handed round by link, writing is not, because the page's "hidden" editor is a
/// client-side gesture and could never protect an open endpoint.
/// </summary>
public class UnmatchedApiTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public UnmatchedApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task The_scoreboard_is_readable_without_logging_in()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/api/unmatched");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ScoreboardResponse>(JsonOptions)).Should().NotBeNull();
    }

    [Fact]
    public async Task Writing_the_scoreboard_requires_the_admin_login()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync("/api/unmatched", new
        {
            fiukWins = 99,
            lanyokWins = 99,
            lastVictor = "Fiuk",
            picks = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_saved_scoreboard_comes_back_whole_and_replaces_what_was_there()
    {
        var client = await CreateAuthenticatedClientAsync();

        var first = await client.PutAsJsonAsync("/api/unmatched", new
        {
            fiukWins = 7,
            lanyokWins = 4,
            lastVictor = "Fiuk",
            picks = new[]
            {
                new { playerName = "Márk", character = "Medusa" },
                new { playerName = "Ádám", character = "Dracula" },
                new { playerName = "Berni", character = "Alice" },
                new { playerName = "Reni", character = "Titania" },
            },
        });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Read it back anonymously - that is how the other players will see it.
        var board = await _factory.CreateClient().GetFromJsonAsync<ScoreboardResponse>("/api/unmatched", JsonOptions);
        board!.FiukWins.Should().Be(7);
        board.LanyokWins.Should().Be(4);
        board.LastVictor.Should().Be("Fiuk");
        board.Picks.Should().HaveCount(4);
        board.Picks.Single(p => p.PlayerName == "Márk").Character.Should().Be("Medusa");

        // A second write replaces rather than accumulates: no history is kept.
        await client.PutAsJsonAsync("/api/unmatched", new
        {
            fiukWins = 7,
            lanyokWins = 5,
            lastVictor = "Lanyok",
            picks = new[] { new { playerName = "Reni", character = "Sinbad" } },
        });

        var updated = await client.GetFromJsonAsync<ScoreboardResponse>("/api/unmatched", JsonOptions);
        updated!.LanyokWins.Should().Be(5);
        updated.LastVictor.Should().Be("Lanyok");
        updated.Picks.Should().ContainSingle(p => p.PlayerName == "Reni" && p.Character == "Sinbad");
    }

    [Fact]
    public async Task A_rejected_scoreboard_leaves_the_stored_one_untouched()
    {
        var client = await CreateAuthenticatedClientAsync();
        await client.PutAsJsonAsync("/api/unmatched", new
        {
            fiukWins = 3,
            lanyokWins = 2,
            lastVictor = "Fiuk",
            picks = new[] { new { playerName = "Márk", character = "Medusa" } },
        });

        var rejected = await client.PutAsJsonAsync("/api/unmatched", new
        {
            fiukWins = 4,
            lanyokWins = 2,
            lastVictor = "Fiuk",
            // The same player twice - the domain refuses the whole write.
            picks = new[]
            {
                new { playerName = "Reni", character = "Alice" },
                new { playerName = "reni", character = "Titania" },
            },
        });
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var board = await client.GetFromJsonAsync<ScoreboardResponse>("/api/unmatched", JsonOptions);
        board!.FiukWins.Should().Be(3);
        board.Picks.Should().ContainSingle(p => p.PlayerName == "Márk");
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ApiFactory.AdminPassword });
        var token = await login.Content.ReadFromJsonAsync<LoginBody>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return client;
    }

    private record ScoreboardResponse(int FiukWins, int LanyokWins, string? LastVictor, List<PickResponse> Picks);
    private record PickResponse(string PlayerName, string Character);
    private record LoginBody(string Token);
}
