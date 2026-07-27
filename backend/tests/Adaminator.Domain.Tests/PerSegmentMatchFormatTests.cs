using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

/// <summary>
/// Match format is settable per bracket segment (Upper/Lower/Grand Final, and Group Stage for
/// Group Stage + Playoff) rather than as one tournament-wide default, and is fixed at bracket-build
/// time - never editable from a match's own result dialog.
/// </summary>
public class PerSegmentMatchFormatTests
{
    private static readonly DateOnly Date = new(2026, 7, 23);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);

    private static Tournament StartedDoubleElimination(
        MatchFormat defaultFormat = MatchFormat.Bo3,
        MatchFormat? upper = null, MatchFormat? lower = null, MatchFormat? grandFinal = null)
    {
        var tournament = Tournament.Create(
            "Cup", Date, null, TournamentType.DoubleElimination, defaultFormat, ScoreType.Games, thirdPlaceEnabled: false, CreatedAt,
            upperBracketFormat: upper, lowerBracketFormat: lower, grandFinalFormat: grandFinal);
        for (var i = 1; i <= 4; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());
        tournament.Start();
        return tournament;
    }

    private static Tournament StartedSingleElimination(
        int participantCount, MatchFormat defaultFormat, MatchFormat? grandFinal = null, bool thirdPlaceEnabled = false)
    {
        var tournament = Tournament.Create(
            "Cup", Date, null, TournamentType.SingleElimination, defaultFormat, ScoreType.Games, thirdPlaceEnabled, CreatedAt,
            grandFinalFormat: grandFinal);
        for (var i = 1; i <= participantCount; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());
        tournament.Start();
        return tournament;
    }

    /// <summary>Decides every group-stage match so the lower seed wins, leaving a strict, tie-free order.</summary>
    private static void DecideGroupStage(Tournament tournament, MatchFormat format, int gamesPerMatch = 1)
    {
        foreach (var match in tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList())
        {
            var seedA = tournament.Participants.First(p => p.Id == match.ParticipantAId).Seed;
            var seedB = tournament.Participants.First(p => p.Id == match.ParticipantBId).Seed;
            var entries = Enumerable.Range(0, gamesPerMatch)
                .Select(_ => new ScoreEntryInput(null, null, seedA < seedB))
                .ToList();
            tournament.CompleteMatch(match.Id, format, ScoreType.Games, entries, CreatedAt);
        }
    }

    [Fact]
    public void Double_elimination_can_give_each_segment_its_own_format()
    {
        var t = StartedDoubleElimination(upper: MatchFormat.Bo5, lower: MatchFormat.Bo1, grandFinal: MatchFormat.Bo3);

        t.Matches.Where(m => m.Segment == BracketSegment.Winner).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo5);
        t.Matches.Where(m => m.Segment == BracketSegment.Loser).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo1);
        t.Matches.Where(m => m.Segment == BracketSegment.GrandFinal).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo3);
    }

    [Fact]
    public void Double_elimination_without_explicit_segment_formats_falls_back_to_the_default_uniformly()
    {
        var t = StartedDoubleElimination(defaultFormat: MatchFormat.Bo3);

        t.UpperBracketFormat.Should().Be(MatchFormat.Bo3);
        t.LowerBracketFormat.Should().Be(MatchFormat.Bo3);
        t.GrandFinalFormat.Should().Be(MatchFormat.Bo3);
        t.Matches.Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo3);
    }

    [Fact]
    public void Group_stage_playoff_can_give_the_group_stage_and_every_playoff_segment_its_own_format()
    {
        var t = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo3, ScoreType.Games, thirdPlaceEnabled: false, CreatedAt,
            groupCount: 2, groupStageMatchFormat: MatchFormat.Bo2,
            upperBracketFormat: MatchFormat.Bo5, lowerBracketFormat: MatchFormat.Bo1, grandFinalFormat: MatchFormat.Bo7);
        for (var i = 1; i <= 8; i++)
        {
            t.AddParticipant($"P{i}");
        }

        t.DrawGroups();
        t.Start();

        t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo2);

        DecideGroupStage(t, MatchFormat.Bo2, gamesPerMatch: 2);

        t.StartPlayoffs();

        t.Matches.Where(m => m.Segment == BracketSegment.Winner).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo5);
        t.Matches.Where(m => m.Segment == BracketSegment.Loser).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo1);
        t.Matches.Where(m => m.Segment == BracketSegment.GrandFinal).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo7);
    }

    [Fact]
    public void Single_elimination_keeps_its_own_final_format_but_ignores_the_bracket_segments_it_has_no_use_for()
    {
        var se = Tournament.Create(
            "Cup", Date, null, TournamentType.SingleElimination, MatchFormat.Bo3, ScoreType.Games, thirdPlaceEnabled: false, CreatedAt,
            upperBracketFormat: MatchFormat.Bo7, lowerBracketFormat: MatchFormat.Bo1, grandFinalFormat: MatchFormat.Bo5, groupStageMatchFormat: MatchFormat.Bo2);

        se.UpperBracketFormat.Should().Be(MatchFormat.Bo3);
        se.LowerBracketFormat.Should().Be(MatchFormat.Bo3);
        se.GroupStageMatchFormat.Should().Be(MatchFormat.Bo3);
        // A Single Elimination tournament ends on a Final, which may be played longer than the rest.
        se.GrandFinalFormat.Should().Be(MatchFormat.Bo5);
    }

    [Fact]
    public void Round_robin_ignores_segment_format_overrides()
    {
        var rr = Tournament.Create(
            "League", Date, null, TournamentType.RoundRobin, MatchFormat.Bo1, ScoreType.Games, thirdPlaceEnabled: false, CreatedAt,
            upperBracketFormat: MatchFormat.Bo7, lowerBracketFormat: MatchFormat.Bo5, grandFinalFormat: MatchFormat.Bo5, groupStageMatchFormat: MatchFormat.Bo2);

        rr.UpperBracketFormat.Should().Be(MatchFormat.Bo1);
        rr.LowerBracketFormat.Should().Be(MatchFormat.Bo1);
        rr.GrandFinalFormat.Should().Be(MatchFormat.Bo1);
        rr.GroupStageMatchFormat.Should().Be(MatchFormat.Bo1);
    }

    [Theory]
    [InlineData(2)]  // the only round is itself the Final
    [InlineData(4)]
    [InlineData(8)]
    public void A_single_elimination_final_is_played_in_the_final_format_and_the_rest_in_the_default(int participantCount)
    {
        var tournament = StartedSingleElimination(participantCount, MatchFormat.Bo1, grandFinal: MatchFormat.Bo5);

        var winner = tournament.Matches.Where(m => m.Segment == BracketSegment.Winner).ToList();
        var finalRound = winner.Max(m => m.Round);

        winner.Single(m => m.Round == finalRound).MatchFormat.Should().Be(MatchFormat.Bo5);
        // NotContain rather than OnlyContain: a two-participant bracket has no earlier round at all.
        winner.Where(m => m.Round < finalRound).Should().NotContain(m => m.MatchFormat != MatchFormat.Bo1);
    }

    [Fact]
    public void A_third_place_match_keeps_the_regular_format_rather_than_the_finals()
    {
        var tournament = StartedSingleElimination(4, MatchFormat.Bo1, grandFinal: MatchFormat.Bo5, thirdPlaceEnabled: true);

        tournament.Matches.Single(m => m.Segment == BracketSegment.ThirdPlace).MatchFormat.Should().Be(MatchFormat.Bo1);
    }

    [Fact]
    public void A_single_elimination_playoff_final_uses_the_grand_final_format()
    {
        var tournament = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.Games, thirdPlaceEnabled: false, CreatedAt,
            groupCount: 2, upperBracketFormat: MatchFormat.Bo1, grandFinalFormat: MatchFormat.Bo7,
            playoffKind: PlayoffKind.SingleElimination);
        for (var i = 1; i <= 8; i++)
        {
            tournament.AddParticipant($"P{i:00}");
        }

        tournament.DrawGroups();
        tournament.Start();
        DecideGroupStage(tournament, MatchFormat.Bo1);

        tournament.StartPlayoffs();

        var winner = tournament.Matches.Where(m => m.Segment == BracketSegment.Winner).ToList();
        var finalRound = winner.Max(m => m.Round);
        winner.Single(m => m.Round == finalRound).MatchFormat.Should().Be(MatchFormat.Bo7);
        winner.Where(m => m.Round < finalRound).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo1);
    }

    [Fact]
    public void UpdateDetails_can_change_segment_formats_while_planned()
    {
        var t = Tournament.Create(
            "Cup", Date, null, TournamentType.DoubleElimination, MatchFormat.Bo3, ScoreType.Games, thirdPlaceEnabled: false, CreatedAt);

        t.UpdateDetails(
            "Cup", Date, null, TournamentType.DoubleElimination, MatchFormat.Bo3, ScoreType.Games, thirdPlaceEnabled: false,
            upperBracketFormat: MatchFormat.Bo5, lowerBracketFormat: MatchFormat.Bo1, grandFinalFormat: MatchFormat.Bo7);

        t.UpperBracketFormat.Should().Be(MatchFormat.Bo5);
        t.LowerBracketFormat.Should().Be(MatchFormat.Bo1);
        t.GrandFinalFormat.Should().Be(MatchFormat.Bo7);
    }
}
