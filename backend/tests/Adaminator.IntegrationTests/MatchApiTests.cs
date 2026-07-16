using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace Adaminator.IntegrationTests;

public class MatchApiTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public MatchApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Full_flow_save_complete_forfeit_and_advance()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);

        var bracket = await GetBracketAsync(client, tournamentId);
        var semifinals = bracket.WinnerRounds.Single(r => r.Round == 1).Matches;
        var semi0 = semifinals[0];
        var semi1 = semifinals[1];

        // Save a partial result -> stays In Progress.
        var save = await client.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{semi0.Id}/result",
            new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true) } });
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterSave = await GetBracketAsync(client, tournamentId);
        MatchIn(afterSave, semi0.Id).Status.Should().Be("InProgress");

        // Complete it -> winner advances into the Final.
        var complete = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{semi0.Id}/complete",
            new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true), Entry(true) } });
        complete.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterComplete = await GetBracketAsync(client, tournamentId);
        var completedSemi0 = MatchIn(afterComplete, semi0.Id);
        completedSemi0.Status.Should().Be("Completed");
        completedSemi0.WinnerId.Should().Be(semi0.ParticipantA!.ParticipantId);
        var finalAfterOne = afterComplete.WinnerRounds.Single(r => r.Round == 2).Matches.Single();
        finalAfterOne.ParticipantA!.ParticipantId.Should().Be(semi0.ParticipantA!.ParticipantId);

        // Forfeit the other semifinal -> its winner advances too.
        var forfeit = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{semi1.Id}/forfeit",
            new { winnerId = semi1.ParticipantB!.ParticipantId });
        forfeit.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterForfeit = await GetBracketAsync(client, tournamentId);
        var completedSemi1 = MatchIn(afterForfeit, semi1.Id);
        completedSemi1.Status.Should().Be("Forfeit");
        var finalAfterBoth = afterForfeit.WinnerRounds.Single(r => r.Round == 2).Matches.Single();
        finalAfterBoth.ParticipantA.Should().NotBeNull();
        finalAfterBoth.ParticipantB.Should().NotBeNull();
    }

    [Fact]
    public async Task Double_elimination_full_flow_routes_through_winner_loser_and_grand_final()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client, "DoubleElimination");

        var bracket = await GetBracketAsync(client, tournamentId);
        bracket.LoserRounds.Should().NotBeEmpty();
        bracket.GrandFinal.Should().NotBeNull();

        var wb1 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[0];
        var wb2 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[1];

        async Task CompleteAsync(Guid matchId) =>
            (await client.PostAsJsonAsync(
                $"/api/tournaments/{tournamentId}/matches/{matchId}/complete",
                new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true), Entry(true) } }))
                .StatusCode.Should().Be(HttpStatusCode.OK);

        await CompleteAsync(wb1.Id);
        await CompleteAsync(wb2.Id);

        var afterWb1And2 = await GetBracketAsync(client, tournamentId);
        var lb1 = afterWb1And2.LoserRounds.Single(r => r.Round == 1).Matches.Single();
        lb1.ParticipantA.Should().NotBeNull();
        lb1.ParticipantB.Should().NotBeNull(); // both round-1 losers routed into the same Loser Bracket match

        await CompleteAsync(lb1.Id);

        var wbFinal = (await GetBracketAsync(client, tournamentId)).WinnerRounds.Single(r => r.Round == 2).Matches.Single();
        await CompleteAsync(wbFinal.Id);

        var afterWbFinal = await GetBracketAsync(client, tournamentId);
        var lbFinal = afterWbFinal.LoserRounds.Single(r => r.Round == 2).Matches.Single();
        lbFinal.ParticipantA.Should().NotBeNull();
        lbFinal.ParticipantB.Should().NotBeNull(); // the Winner Bracket Final's loser reached the Loser Bracket Final

        await CompleteAsync(lbFinal.Id);

        var afterLbFinal = await GetBracketAsync(client, tournamentId);
        afterLbFinal.ThirdPlacePodium.Should().NotBeNull();
        var grandFinal = afterLbFinal.GrandFinal!;
        grandFinal.ParticipantA.Should().NotBeNull();
        grandFinal.ParticipantB.Should().NotBeNull();

        await CompleteAsync(grandFinal.Id);

        var final = await GetBracketAsync(client, tournamentId);
        final.GrandFinal!.WinnerId.Should().NotBeNull();
        (await client.GetFromJsonAsync<TournamentStatusResponse>($"/api/tournaments/{tournamentId}", JsonOptions))!
            .Status.Should().Be("Finished");
    }

    [Fact]
    public async Task Complete_with_a_non_decisive_score_is_rejected()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);
        var bracket = await GetBracketAsync(client, tournamentId);
        var semi0 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[0];

        var complete = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{semi0.Id}/complete",
            new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true), Entry(false) } });

        complete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Acting_on_a_match_with_an_unresolved_slot_is_rejected()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);
        var bracket = await GetBracketAsync(client, tournamentId);
        var final = bracket.WinnerRounds.Single(r => r.Round == 2).Matches.Single();

        var save = await client.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{final.Id}/result",
            new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true) } });

        save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Winner_only_scoring_is_rejected_for_a_non_bo1_match()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);
        var bracket = await GetBracketAsync(client, tournamentId);
        var semi0 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[0];

        var save = await client.PutAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{semi0.Id}/result",
            new { matchFormat = "Bo3", scoreType = "WinnerOnly", entries = new[] { Entry(true) } });

        save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Forfeit_without_a_valid_winner_is_rejected()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);

        var forfeit = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{Guid.NewGuid()}/forfeit",
            new { winnerId = Guid.Empty });

        forfeit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Undo_reverts_the_latest_completed_match_but_not_an_earlier_one()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);
        var bracket = await GetBracketAsync(client, tournamentId);
        var semi0 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[0];
        var semi1 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[1];

        await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{semi0.Id}/complete",
            new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true), Entry(true) } });
        await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{semi1.Id}/complete",
            new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true), Entry(true) } });

        // semi0 is no longer the latest completed match.
        var undoEarlier = await client.PostAsync($"/api/tournaments/{tournamentId}/matches/{semi0.Id}/undo", null);
        undoEarlier.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var undoLatest = await client.PostAsync($"/api/tournaments/{tournamentId}/matches/{semi1.Id}/undo", null);
        undoLatest.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUndo = await GetBracketAsync(client, tournamentId);
        MatchIn(afterUndo, semi1.Id).Status.Should().Be("InProgress");
        var final = afterUndo.WinnerRounds.Single(r => r.Round == 2).Matches.Single();
        final.ParticipantB.Should().BeNull();
    }

    [Fact]
    public async Task Anonymous_requests_to_match_endpoints_are_rejected()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);
        var bracket = await GetBracketAsync(client, tournamentId);
        var semi0 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[0];

        var anonymous = _factory.CreateClient();
        var body = new { matchFormat = "Bo3", scoreType = "Games", entries = Array.Empty<object>() };

        (await anonymous.PutAsJsonAsync($"/api/tournaments/{tournamentId}/matches/{semi0.Id}/result", body))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches/{semi0.Id}/complete", body))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"/api/tournaments/{tournamentId}/matches/{semi0.Id}/forfeit", new { winnerId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/tournaments/{tournamentId}/matches/{semi0.Id}/undo", null))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static object Entry(bool participantAWon) => new { scoreA = (int?)null, scoreB = (int?)null, participantAWon };

    private static MatchResponse MatchIn(BracketResponse bracket, Guid matchId) =>
        bracket.WinnerRounds.SelectMany(r => r.Matches).Concat(bracket.ThirdPlace is null ? [] : [bracket.ThirdPlace])
            .Single(m => m.Id == matchId);

    private async Task<BracketResponse> GetBracketAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<BracketResponse>($"/api/tournaments/{tournamentId}/bracket", JsonOptions))!;

    private async Task<Guid> CreateStartedFourPlayerTournamentAsync(HttpClient client, string type = "SingleElimination")
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Cup {Guid.NewGuid():N}",
            date = "2026-07-14",
            type,
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = false
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions);
        var tournamentId = created!.Id;

        foreach (var name in new[] { "A", "B", "C", "D" })
        {
            await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
        }

        await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/generate", null);
        await client.PostAsync($"/api/tournaments/{tournamentId}/start", null);
        return tournamentId;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = ApiFactory.AdminPassword });
        var token = await login.Content.ReadFromJsonAsync<LoginBody>(JsonOptions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.Token);
        return client;
    }

    private record CreatedTournament(Guid Id, string PublicToken);
    private record TournamentStatusResponse(string Status);
    private record LoginBody(string Token);
    private record ParticipantSlotResponse(Guid ParticipantId, string Name);
    private record MatchResponse(
        Guid Id, string Segment, int Round, int IndexInRound,
        ParticipantSlotResponse? ParticipantA, ParticipantSlotResponse? ParticipantB,
        string Status, Guid? WinnerId, string MatchFormat, string? ScoreType,
        List<object> Entries, int AggregateScoreA, int AggregateScoreB,
        DateTimeOffset? CompletedAt, bool CanUndo);
    private record RoundResponse(int Round, string Title, List<MatchResponse> Matches);
    private record BracketResponse(
        List<RoundResponse> WinnerRounds,
        List<RoundResponse> LoserRounds,
        MatchResponse? GrandFinal,
        MatchResponse? ThirdPlace,
        ParticipantSlotResponse? ThirdPlacePodium);
}
