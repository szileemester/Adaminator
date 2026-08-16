using System.Net;
using System.Net.Http.Json;
using Adaminator.Application.Tournaments;
using Adaminator.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Adaminator.IntegrationTests;

/// <summary>
/// The tournament carries a row version so two requests working on the same tournament cannot silently
/// overwrite each other. Almost every write it guards changes only children - a match result, a new
/// playoff bracket, a replaced roster - and EF puts the version in the WHERE clause only when a
/// *tournament* column is dirty, so those writes have to bring the root into the save deliberately.
/// These load the same aggregate twice, as two overlapping requests would, and save both.
/// </summary>
public class ConcurrencyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ConcurrencyTests(ApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Completing a match writes no tournament column at all - only the match and its games.</summary>
    [Fact]
    public async Task Two_overlapping_match_results_cannot_both_be_saved()
    {
        var tournamentId = await StartedFourPlayerAsync();

        using var first = _factory.Services.CreateScope();
        using var second = _factory.Services.CreateScope();
        var repositoryA = first.ServiceProvider.GetRequiredService<ITournamentRepository>();
        var repositoryB = second.ServiceProvider.GetRequiredService<ITournamentRepository>();

        // Both read before either writes, which is the whole point - each holds the same row version.
        var tournamentA = (await repositoryA.GetByIdAsync(tournamentId))!;
        var tournamentB = (await repositoryB.GetByIdAsync(tournamentId))!;

        // Different matches each: nothing they write collides directly, so the only thing that can
        // separate them is the version on the tournament they both read.
        CompletePendingMatch(tournamentA, 0);
        CompletePendingMatch(tournamentB, 1);

        await repositoryA.SaveChangesAsync();

        await repositoryB.Invoking(r => r.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    /// <summary>
    /// The costly one: two "start playoffs" requests both see a group stage that has not been played off
    /// yet, and both build a whole bracket. Left unguarded that leaves two Grand Finals in one
    /// tournament, which every later bracket read then fails on - with no way back but deleting it.
    /// </summary>
    [Fact]
    public async Task The_playoff_cannot_be_drawn_twice_by_overlapping_requests()
    {
        var tournamentId = await StartedGroupStageAsync();

        using var first = _factory.Services.CreateScope();
        using var second = _factory.Services.CreateScope();
        var repositoryA = first.ServiceProvider.GetRequiredService<ITournamentRepository>();
        var repositoryB = second.ServiceProvider.GetRequiredService<ITournamentRepository>();

        var tournamentA = (await repositoryA.GetByIdAsync(tournamentId))!;
        var tournamentB = (await repositoryB.GetByIdAsync(tournamentId))!;

        tournamentA.StartPlayoffs();
        tournamentB.StartPlayoffs();

        await repositoryA.SaveChangesAsync();

        await repositoryB.Invoking(r => r.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateConcurrencyException>();

        using var check = _factory.Services.CreateScope();
        var saved = (await check.ServiceProvider.GetRequiredService<ITournamentRepository>()
            .GetByIdAsync(tournamentId))!;
        saved.Matches.Count(m => m.Segment == BracketSegment.GrandFinal).Should().Be(1);
    }

    private static void CompletePendingMatch(Domain.Entities.Tournament tournament, int index)
    {
        var match = tournament.Matches
            .Where(m => m.Status == MatchStatus.Pending && m.ParticipantAId is not null && m.ParticipantBId is not null)
            .OrderBy(m => m.Round).ThenBy(m => m.IndexInRound)
            .ElementAt(index);
        tournament.CompleteMatch(
            match.Id, match.MatchFormat, match.ScoreType,
            new List<Domain.Entities.ScoreEntryInput> { new(null, null, true) },
            DateTimeOffset.UtcNow);
    }

    private async Task<Guid> StartedFourPlayerAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tournamentId = await CreateAsync(client, "SingleElimination", groupCount: 0);
        await AddParticipantsAsync(client, tournamentId, 4);
        await StartAsync(client, tournamentId);
        return tournamentId;
    }

    /// <summary>A group stage played out in full, so the playoff is ready to be drawn but has not been.</summary>
    private async Task<Guid> StartedGroupStageAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tournamentId = await CreateAsync(client, "GroupStagePlayoff", groupCount: 2);
        await AddParticipantsAsync(client, tournamentId, 8);
        await StartAsync(client, tournamentId, drawGroups: true);

        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITournamentRepository>();
        var tournament = (await repository.GetByIdAsync(tournamentId))!;
        foreach (var match in tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList())
        {
            // Seeded winner every time, so the groups end in a strict order with no tie to break.
            var seedA = tournament.Participants.First(p => p.Id == match.ParticipantAId).Seed;
            var seedB = tournament.Participants.First(p => p.Id == match.ParticipantBId).Seed;
            tournament.CompleteMatch(
                match.Id, match.MatchFormat, match.ScoreType,
                new List<Domain.Entities.ScoreEntryInput> { new(null, null, seedA < seedB) },
                DateTimeOffset.UtcNow);
        }

        await repository.SaveChangesAsync();
        return tournamentId;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string type, int groupCount)
    {
        var response = await client.PostAsJsonAsync("/api/tournaments", new
        {
            name = $"Race {Guid.NewGuid():N}",
            date = "2026-08-01",
            type,
            defaultMatchFormat = "Bo1",
            groupStageMatchFormat = "Bo1",
            thirdPlaceEnabled = false,
            groupCount
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TournamentResponse>(ApiFactory.JsonOptions))!.Id;
    }

    private static async Task AddParticipantsAsync(HttpClient client, Guid tournamentId, int count)
    {
        var response = await client.PutAsJsonAsync($"/api/tournaments/{tournamentId}/participants", new
        {
            participants = Enumerable.Range(1, count).Select(i => new { id = (Guid?)null, name = $"P{i}" }).ToArray()
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task StartAsync(HttpClient client, Guid tournamentId, bool drawGroups = false)
    {
        // A group stage is drawn, every other shape is generated; both then start the same way.
        var build = drawGroups ? "draw-groups" : "generate";
        (await client.PostAsync($"/api/tournaments/{tournamentId}/bracket/{build}", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync($"/api/tournaments/{tournamentId}/start", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private record TournamentResponse(Guid Id);
}
