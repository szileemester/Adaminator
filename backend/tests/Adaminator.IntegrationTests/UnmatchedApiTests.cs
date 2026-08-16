using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Adaminator.IntegrationTests;

/// <summary>
/// The house Unmatched ladder. It is the one open corner of the API: both reading and writing are
/// anonymous, so any of the players can record a result from the link. Every test here therefore uses
/// a plain client - what still guards the row is the domain's validation, not the login.
/// </summary>
public class UnmatchedApiTests : IClassFixture<ApiFactory>
{
    // The API's own serializer settings, so the suite reads responses exactly as the API writes them.
    private static readonly JsonSerializerOptions JsonOptions = ApiFactory.JsonOptions;

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
    public async Task The_scoreboard_is_writable_without_logging_in()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync("/api/unmatched", new
        {
            fiukWins = 2,
            lanyokWins = 1,
            lastVictor = "Fiuk",
            picks = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var board = await anonymous.GetFromJsonAsync<ScoreboardResponse>("/api/unmatched", JsonOptions);
        board!.FiukWins.Should().Be(2);
    }

    [Fact]
    public async Task A_saved_scoreboard_comes_back_whole_and_replaces_what_was_there()
    {
        var client = _factory.CreateClient();

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

        var board = await client.GetFromJsonAsync<ScoreboardResponse>("/api/unmatched", JsonOptions);
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
        var client = _factory.CreateClient();
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

    private record ScoreboardResponse(int FiukWins, int LanyokWins, string? LastVictor, List<PickResponse> Picks);
    private record PickResponse(string PlayerName, string Character);
}
