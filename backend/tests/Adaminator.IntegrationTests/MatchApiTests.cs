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

        await CompleteMatchAsync(client, tournamentId, wb1.Id);
        await CompleteMatchAsync(client, tournamentId, wb2.Id);

        var afterWb1And2 = await GetBracketAsync(client, tournamentId);
        var lb1 = afterWb1And2.LoserRounds.Single(r => r.Round == 1).Matches.Single();
        lb1.ParticipantA.Should().NotBeNull();
        lb1.ParticipantB.Should().NotBeNull(); // both round-1 losers routed into the same Loser Bracket match

        await CompleteMatchAsync(client, tournamentId, lb1.Id);

        var wbFinal = (await GetBracketAsync(client, tournamentId)).WinnerRounds.Single(r => r.Round == 2).Matches.Single();
        await CompleteMatchAsync(client, tournamentId, wbFinal.Id);

        var afterWbFinal = await GetBracketAsync(client, tournamentId);
        var lbFinal = afterWbFinal.LoserRounds.Single(r => r.Round == 2).Matches.Single();
        lbFinal.ParticipantA.Should().NotBeNull();
        lbFinal.ParticipantB.Should().NotBeNull(); // the Winner Bracket Final's loser reached the Loser Bracket Final

        await CompleteMatchAsync(client, tournamentId, lbFinal.Id);

        var afterLbFinal = await GetBracketAsync(client, tournamentId);
        afterLbFinal.ThirdPlacePodium.Should().NotBeNull();
        var grandFinal = afterLbFinal.GrandFinal!;
        grandFinal.ParticipantA.Should().NotBeNull();
        grandFinal.ParticipantB.Should().NotBeNull();

        await CompleteMatchAsync(client, tournamentId, grandFinal.Id);

        var final = await GetBracketAsync(client, tournamentId);
        final.GrandFinal!.WinnerId.Should().NotBeNull();
        final.CanFinish.Should().BeTrue();

        var championId = final.GrandFinal!.WinnerId!.Value;
        var runnerUpId = grandFinal.ParticipantA!.ParticipantId == championId
            ? grandFinal.ParticipantB!.ParticipantId
            : grandFinal.ParticipantA!.ParticipantId;
        final.Placements.Should().ContainSingle(g => g.Label == "Champion" && g.RankStart == 1 && g.RankEnd == 1 && g.Participants.Single().ParticipantId == championId);
        final.Placements.Should().ContainSingle(g => g.Label == "Runner-up" && g.RankStart == 2 && g.RankEnd == 2 && g.Participants.Single().ParticipantId == runnerUpId);
        final.Placements.Should().ContainSingle(g => g.Label == "3rd Place" && g.RankStart == 3 && g.RankEnd == 3 && g.Participants.Single().ParticipantId == afterLbFinal.ThirdPlacePodium!.ParticipantId);
        (await client.GetFromJsonAsync<TournamentStatusResponse>($"/api/tournaments/{tournamentId}", JsonOptions))!
            .Status.Should().Be("Running");

        var finish = await client.PostAsync($"/api/tournaments/{tournamentId}/finish", null);
        finish.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<TournamentStatusResponse>($"/api/tournaments/{tournamentId}", JsonOptions))!
            .Status.Should().Be("Finished");
    }

    [Fact]
    public async Task Placements_fill_in_progressively_as_single_elimination_rounds_are_decided()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedTournamentAsync(client, new[] { "A", "B", "C", "D", "E", "F", "G", "H" });

        async Task<Guid> CompleteAsync(Guid matchId)
        {
            await CompleteMatchAsync(client, tournamentId, matchId);
            return (await GetBracketAsync(client, tournamentId)).WinnerRounds.SelectMany(r => r.Matches).Single(m => m.Id == matchId).WinnerId!.Value;
        }

        // The whole results table exists from the moment the bracket does; rows fill in as places are decided.
        var beforeAnyResult = await GetBracketAsync(client, tournamentId);
        beforeAnyResult.Placements.Select(g => g.Label).Should()
            .Equal("Champion", "Runner-up", "Semifinalists", "Eliminated in Quarterfinals");
        beforeAnyResult.Placements.Should().OnlyContain(g => g.Participants.Count == 0);

        var quarterfinals = beforeAnyResult.WinnerRounds.Single(r => r.Round == 1).Matches;
        var quarterfinalLosers = new List<Guid>();
        foreach (var qf in quarterfinals)
        {
            var winnerId = await CompleteAsync(qf.Id);
            quarterfinalLosers.Add(winnerId == qf.ParticipantA!.ParticipantId ? qf.ParticipantB!.ParticipantId : qf.ParticipantA!.ParticipantId);
        }

        var afterQuarterfinals = await GetBracketAsync(client, tournamentId);
        afterQuarterfinals.Placements.Should().ContainSingle(g =>
            g.Label == "Eliminated in Quarterfinals" && g.RankStart == 5 && g.RankEnd == 8 &&
            g.Participants.Select(p => p.ParticipantId).ToHashSet().SetEquals(quarterfinalLosers));
        afterQuarterfinals.Placements.Should()
            .OnlyContain(g => g.Participants.Count == 0 || g.Label == "Eliminated in Quarterfinals");

        var semifinals = afterQuarterfinals.WinnerRounds.Single(r => r.Round == 2).Matches;
        var semifinalLosers = new List<Guid>();
        foreach (var sf in semifinals)
        {
            var winnerId = await CompleteAsync(sf.Id);
            semifinalLosers.Add(winnerId == sf.ParticipantA!.ParticipantId ? sf.ParticipantB!.ParticipantId : sf.ParticipantA!.ParticipantId);
        }

        var afterSemifinals = await GetBracketAsync(client, tournamentId);
        afterSemifinals.Placements.Should().ContainSingle(g =>
            g.Label == "Semifinalists" && g.RankStart == 3 && g.RankEnd == 4 &&
            g.Participants.Select(p => p.ParticipantId).ToHashSet().SetEquals(semifinalLosers));
        afterSemifinals.Placements.Should().ContainSingle(g => g.Label == "Eliminated in Quarterfinals");
        afterSemifinals.Placements.Should()
            .OnlyContain(g => g.Participants.Count > 0 || g.Label == "Champion" || g.Label == "Runner-up");

        var final = afterSemifinals.WinnerRounds.Single(r => r.Round == 3).Matches.Single();
        var championId = await CompleteAsync(final.Id);
        var runnerUpId = championId == final.ParticipantA!.ParticipantId ? final.ParticipantB!.ParticipantId : final.ParticipantA!.ParticipantId;

        var afterFinal = await GetBracketAsync(client, tournamentId);
        afterFinal.Placements.Should().ContainSingle(g => g.Label == "Champion" && g.RankStart == 1 && g.RankEnd == 1 && g.Participants.Single().ParticipantId == championId);
        afterFinal.Placements.Should().ContainSingle(g => g.Label == "Runner-up" && g.RankStart == 2 && g.RankEnd == 2 && g.Participants.Single().ParticipantId == runnerUpId);
        afterFinal.Placements.Should().ContainSingle(g => g.Label == "Semifinalists");
        afterFinal.Placements.Should().ContainSingle(g => g.Label == "Eliminated in Quarterfinals");
    }

    [Fact]
    public async Task Third_and_fourth_place_rows_exist_from_the_start_and_fill_in_when_that_match_is_decided()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client, thirdPlaceEnabled: true);
        var bracket = await GetBracketAsync(client, tournamentId);

        // A Third Place match fixes the table's shape up front: 3rd and 4th are separate rows from the
        // start, never a "Semifinalists" row that later splits in two.
        bracket.Placements.Select(g => g.Label).Should().Equal("Champion", "Runner-up", "3rd Place", "4th Place");
        bracket.Placements.Should().OnlyContain(g => g.Participants.Count == 0);

        var semi0 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[0];
        var semi1 = bracket.WinnerRounds.Single(r => r.Round == 1).Matches[1];
        await CompleteMatchAsync(client, tournamentId, semi0.Id);
        await CompleteMatchAsync(client, tournamentId, semi1.Id);

        var beforeThirdPlace = await GetBracketAsync(client, tournamentId);
        beforeThirdPlace.Placements.Should().NotContain(g => g.Label == "Semifinalists");
        beforeThirdPlace.Placements.Should()
            .OnlyContain(g => g.Participants.Count == 0 || g.Label == "Champion" || g.Label == "Runner-up");

        var thirdPlace = beforeThirdPlace.ThirdPlace!;
        var thirdPlaceWinnerId = thirdPlace.ParticipantA!.ParticipantId;
        await CompleteMatchAsync(client, tournamentId, thirdPlace.Id);

        var afterThirdPlace = await GetBracketAsync(client, tournamentId);
        afterThirdPlace.Placements.Should().ContainSingle(g => g.Label == "3rd Place" && g.RankStart == 3 && g.RankEnd == 3 && g.Participants.Single().ParticipantId == thirdPlaceWinnerId);
        afterThirdPlace.Placements.Should().ContainSingle(g => g.Label == "4th Place" && g.RankStart == 4 && g.RankEnd == 4 && g.Participants.Single().ParticipantId == thirdPlace.ParticipantB!.ParticipantId);
    }

    [Fact]
    public async Task Finish_is_rejected_until_the_final_is_decided_and_succeeds_once_it_is()
    {
        var client = await CreateAuthenticatedClientAsync();
        var tournamentId = await CreateStartedFourPlayerTournamentAsync(client);

        var tooEarly = await client.PostAsync($"/api/tournaments/{tournamentId}/finish", null);
        tooEarly.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bracket = await GetBracketAsync(client, tournamentId);
        foreach (var semi in bracket.WinnerRounds.Single(r => r.Round == 1).Matches)
        {
            await CompleteMatchAsync(client, tournamentId, semi.Id);
        }

        var final = (await GetBracketAsync(client, tournamentId)).WinnerRounds.Single(r => r.Round == 2).Matches.Single();
        await CompleteMatchAsync(client, tournamentId, final.Id);

        (await client.GetFromJsonAsync<TournamentStatusResponse>($"/api/tournaments/{tournamentId}", JsonOptions))!
            .Status.Should().Be("Running");

        var finish = await client.PostAsync($"/api/tournaments/{tournamentId}/finish", null);
        finish.StatusCode.Should().Be(HttpStatusCode.OK);
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

        await CompleteMatchAsync(client, tournamentId, semi0.Id);
        await CompleteMatchAsync(client, tournamentId, semi1.Id);

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

    /// <summary>Completes a match decisively (Bo3, two Games wins for participant A) and asserts the request succeeded.</summary>
    private static async Task CompleteMatchAsync(HttpClient client, Guid tournamentId, Guid matchId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{matchId}/complete",
            new { matchFormat = "Bo3", scoreType = "Games", entries = new[] { Entry(true), Entry(true) } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Completes a Bo1 / Winner Only match for participant A (the Group Stage + Playoff default scoring).</summary>
    private static async Task CompleteWinnerOnlyAsync(HttpClient client, Guid tournamentId, Guid matchId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{matchId}/complete",
            new { matchFormat = "Bo1", scoreType = "WinnerOnly", entries = new[] { Entry(true) } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Completes a group match with the alphabetically-first participant winning, so each group ends in a strict, tie-free order (no tie-breaker needed).</summary>
    private static async Task CompleteGroupSeededAsync(HttpClient client, Guid tournamentId, MatchResponse match)
    {
        var aWon = string.CompareOrdinal(match.ParticipantA!.Name, match.ParticipantB!.Name) < 0;
        var response = await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{match.Id}/complete",
            new { matchFormat = "Bo1", scoreType = "WinnerOnly", entries = new[] { Entry(aWon) } });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Repeatedly completes any actionable playoff match (participant A wins) until the Grand Final is decided.</summary>
    private async Task PlayOutPlayoffAsync(HttpClient client, Guid tournamentId)
    {
        for (var guard = 0; guard < 200; guard++)
        {
            var bracket = await GetBracketAsync(client, tournamentId);
            var playoffMatches = bracket.WinnerRounds
                .Concat(bracket.LoserRounds)
                .SelectMany(r => r.Matches)
                .Concat(bracket.GrandFinal is null ? Array.Empty<MatchResponse>() : new[] { bracket.GrandFinal });

            var next = playoffMatches.FirstOrDefault(m =>
                m.Status == "Pending" && m.ParticipantA is not null && m.ParticipantB is not null);
            if (next is null)
            {
                return;
            }

            await CompleteWinnerOnlyAsync(client, tournamentId, next.Id);
        }

        throw new InvalidOperationException("Playoff did not resolve within the iteration guard.");
    }

    [Fact]
    public async Task Group_stage_playoff_full_flow_draws_plays_groups_seeds_and_finishes_the_playoff()
    {
        var client = await CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Major {Guid.NewGuid():N}",
            date = "2026-07-18",
            type = "GroupStagePlayoff",
            defaultMatchFormat = "Bo1",
            thirdPlaceEnabled = false,
            defaultScoreType = "WinnerOnly",
            groupCount = 2
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tournamentId = (await create.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions))!.Id;

        foreach (var name in new[] { "A", "B", "C", "D", "E", "F", "G", "H" })
        {
            await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
        }

        (await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/draw-groups", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/tournaments/{tournamentId}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The playoff cannot start until every group match is decided.
        (await client.PostAsync($"/api/tournaments/{tournamentId}/start-playoffs", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var groupBracket = await GetBracketAsync(client, tournamentId);
        groupBracket.Groups.Should().HaveCount(2);
        foreach (var groupMatch in groupBracket.Groups.SelectMany(g => g.Rounds).SelectMany(r => r.Matches))
        {
            await CompleteGroupSeededAsync(client, tournamentId, groupMatch);
        }

        var afterGroups = await GetBracketAsync(client, tournamentId);
        afterGroups.CanStartPlayoffs.Should().BeTrue();
        afterGroups.Groups.Should().OnlyContain(g => g.Standings.Count == 4);

        (await client.PostAsync($"/api/tournaments/{tournamentId}/start-playoffs", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var playoff = await GetBracketAsync(client, tournamentId);
        playoff.WinnerRounds.SelectMany(r => r.Matches).Should().NotBeEmpty();
        playoff.LoserRounds.SelectMany(r => r.Matches).Should().NotBeEmpty();
        playoff.GrandFinal.Should().NotBeNull();
        // No Winner round 1 - the upper seeds enter at round 2.
        playoff.WinnerRounds.Should().NotContain(r => r.Round == 1);

        await PlayOutPlayoffAsync(client, tournamentId);

        (await GetBracketAsync(client, tournamentId)).CanFinish.Should().BeTrue();
        (await client.PostAsync($"/api/tournaments/{tournamentId}/finish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<TournamentStatusResponse>($"/api/tournaments/{tournamentId}", JsonOptions))!
            .Status.Should().Be("Finished");
    }

    [Fact]
    public async Task Group_stage_playoff_tiebreaker_flow_resolves_a_group_cycle_before_the_playoff()
    {
        var client = await CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Major {Guid.NewGuid():N}",
            date = "2026-07-18",
            type = "GroupStagePlayoff",
            defaultMatchFormat = "Bo1",
            thirdPlaceEnabled = false,
            defaultScoreType = "WinnerOnly",
            groupCount = 2,
            tiebreakerPolicy = "ComputedThenMatch"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tournamentId = (await create.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions))!.Id;

        foreach (var name in new[] { "A", "B", "C", "D", "E", "F", "G", "H" })
        {
            await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
        }

        (await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/draw-groups", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/tournaments/{tournamentId}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // "Participant A always wins" leaves each group's lower three seeds in a rock-paper-scissors cycle straddling the Upper/Lower line.
        var groupBracket = await GetBracketAsync(client, tournamentId);
        foreach (var groupMatch in groupBracket.Groups.SelectMany(g => g.Rounds).SelectMany(r => r.Matches))
        {
            await CompleteWinnerOnlyAsync(client, tournamentId, groupMatch.Id);
        }

        var afterGroups = await GetBracketAsync(client, tournamentId);
        afterGroups.NeedsTiebreakers.Should().BeTrue();
        afterGroups.CanStartPlayoffs.Should().BeFalse();
        (await client.PostAsync($"/api/tournaments/{tournamentId}/start-playoffs", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsync($"/api/tournaments/{tournamentId}/start-tiebreakers", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var withTiebreakers = await GetBracketAsync(client, tournamentId);
        withTiebreakers.NeedsTiebreakers.Should().BeFalse(); // one-shot
        withTiebreakers.CanStartPlayoffs.Should().BeFalse(); // ...but not yet decided
        var tiebreakerMatches = withTiebreakers.Groups.SelectMany(g => g.TiebreakerRounds).SelectMany(r => r.Matches).ToList();
        tiebreakerMatches.Should().HaveCount(6); // 3 per group

        foreach (var match in tiebreakerMatches)
        {
            await CompleteWinnerOnlyAsync(client, tournamentId, match.Id);
        }

        var resolved = await GetBracketAsync(client, tournamentId);
        resolved.CanStartPlayoffs.Should().BeTrue();
        (await client.PostAsync($"/api/tournaments/{tournamentId}/start-playoffs", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Best_of_two_group_records_a_draw_and_ranks_by_games_won()
    {
        var client = await CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Major {Guid.NewGuid():N}",
            date = "2026-07-21",
            type = "GroupStagePlayoff",
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled = false,
            defaultScoreType = "Games",
            groupCount = 2,
            groupStageFormat = "BestOfTwo"
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var tournamentId = (await create.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions))!.Id;

        foreach (var name in new[] { "A", "B", "C", "D", "E", "F", "G", "H" })
        {
            await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
        }

        (await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/draw-groups", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/tournaments/{tournamentId}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var group = await GetBracketAsync(client, tournamentId);
        var groupMatches = group.Groups.SelectMany(g => g.Rounds).SelectMany(r => r.Matches).ToList();
        groupMatches.Should().OnlyContain(m => m.MatchFormat == "Bo2");

        // Play the first match as a 1-1 draw (two games, one each) over the wire.
        var draw = groupMatches[0];
        (await client.PostAsJsonAsync(
            $"/api/tournaments/{tournamentId}/matches/{draw.Id}/complete",
            new { matchFormat = "Bo2", scoreType = "Games", entries = new[] { Entry(true), Entry(false) } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var afterDraw = await GetBracketAsync(client, tournamentId);
        var completed = afterDraw.Groups.SelectMany(g => g.Rounds).SelectMany(r => r.Matches).Single(m => m.Id == draw.Id);
        completed.Status.Should().Be("Completed");
        completed.WinnerId.Should().BeNull(); // a draw
        // Both drawn participants show one game won and no match win.
        var drawnIds = new[] { draw.ParticipantA!.ParticipantId, draw.ParticipantB!.ParticipantId };
        var drawnRows = afterDraw.Groups.SelectMany(g => g.Standings).Where(s => drawnIds.Contains(s.ParticipantId)).ToList();
        drawnRows.Should().OnlyContain(s => s.GamesWon == 1 && s.Wins == 0 && s.Played == 1);
    }

    [Fact]
    public async Task Group_stage_playoff_rejects_fewer_than_two_groups()
    {
        var client = await CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = "Bad Major",
            date = "2026-07-18",
            type = "GroupStagePlayoff",
            defaultMatchFormat = "Bo1",
            thirdPlaceEnabled = false,
            defaultScoreType = "WinnerOnly",
            groupCount = 1
        });

        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Group_draw_rejects_a_roster_too_small_for_a_playoff()
    {
        var client = await CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Major {Guid.NewGuid():N}",
            date = "2026-07-18",
            type = "GroupStagePlayoff",
            defaultMatchFormat = "Bo1",
            thirdPlaceEnabled = false,
            defaultScoreType = "WinnerOnly",
            groupCount = 2
        });
        var tournamentId = (await create.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions))!.Id;

        foreach (var name in new[] { "A", "B", "C" }) // fewer than the smallest playoff capacity
        {
            await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
        }

        (await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/draw-groups", null))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Group_draw_accepts_a_roster_that_is_not_a_power_of_two()
    {
        var client = await CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Major {Guid.NewGuid():N}",
            date = "2026-07-18",
            type = "GroupStagePlayoff",
            defaultMatchFormat = "Bo1",
            thirdPlaceEnabled = false,
            defaultScoreType = "WinnerOnly",
            groupCount = 2
        });
        var tournamentId = (await create.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions))!.Id;

        foreach (var name in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I" }) // 9 -> groups of 5 and 4
        {
            await client.PostAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new { name });
        }

        (await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/draw-groups", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/tournaments/{tournamentId}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var bracket = await GetBracketAsync(client, tournamentId);
        bracket.Groups.Select(g => g.Standings.Count).OrderByDescending(c => c).Should().Equal(5, 4);
        // Only 8 of the 9 reach the playoff, so the last place is its own row from the start.
        bracket.Placements.Should().ContainSingle(g =>
            g.Label == "Eliminated in the group stage" && g.RankStart == 9 && g.RankEnd == 9);
    }

    private static MatchResponse MatchIn(BracketResponse bracket, Guid matchId) =>
        bracket.WinnerRounds.SelectMany(r => r.Matches).Concat(bracket.ThirdPlace is null ? [] : [bracket.ThirdPlace])
            .Single(m => m.Id == matchId);

    private async Task<BracketResponse> GetBracketAsync(HttpClient client, Guid tournamentId) =>
        (await client.GetFromJsonAsync<BracketResponse>($"/api/tournaments/{tournamentId}/bracket", JsonOptions))!;

    private async Task<Guid> CreateStartedFourPlayerTournamentAsync(HttpClient client, string type = "SingleElimination", bool thirdPlaceEnabled = false) =>
        await CreateStartedTournamentAsync(client, new[] { "A", "B", "C", "D" }, type, thirdPlaceEnabled);

    private async Task<Guid> CreateStartedTournamentAsync(
        HttpClient client, IReadOnlyList<string> participantNames, string type = "SingleElimination", bool thirdPlaceEnabled = false)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Cup {Guid.NewGuid():N}",
            date = "2026-07-14",
            type,
            defaultMatchFormat = "Bo3",
            thirdPlaceEnabled
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedTournament>(JsonOptions);
        var tournamentId = created!.Id;

        foreach (var name in participantNames)
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
    private record PlacementGroupResponse(int RankStart, int RankEnd, string Label, List<ParticipantSlotResponse> Participants);
    private record StandingRowResponse(int Rank, Guid ParticipantId, string Name, int Played, int Wins, int Losses, int GamesWon);
    private record GroupResponse(int GroupIndex, List<RoundResponse> Rounds, List<StandingRowResponse> Standings, List<RoundResponse> TiebreakerRounds);
    private record BracketResponse(
        List<RoundResponse> WinnerRounds,
        List<RoundResponse> LoserRounds,
        MatchResponse? GrandFinal,
        MatchResponse? ThirdPlace,
        ParticipantSlotResponse? ThirdPlacePodium,
        List<PlacementGroupResponse> Placements,
        List<GroupResponse> Groups,
        List<RoundResponse> TiebreakerRounds,
        bool NeedsTiebreakers,
        bool CanStartPlayoffs,
        bool CanFinish);
}
