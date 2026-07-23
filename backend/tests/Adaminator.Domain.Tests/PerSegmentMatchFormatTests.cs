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

        foreach (var m in t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList())
        {
            var seedA = t.Participants.First(p => p.Id == m.ParticipantAId).Seed;
            var seedB = t.Participants.First(p => p.Id == m.ParticipantBId).Seed;
            var entries = new List<ScoreEntryInput> { new(null, null, seedA < seedB), new(null, null, seedA < seedB) };
            t.CompleteMatch(m.Id, MatchFormat.Bo2, ScoreType.Games, entries, CreatedAt);
        }

        t.StartPlayoffs();

        t.Matches.Where(m => m.Segment == BracketSegment.Winner).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo5);
        t.Matches.Where(m => m.Segment == BracketSegment.Loser).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo1);
        t.Matches.Where(m => m.Segment == BracketSegment.GrandFinal).Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo7);
    }

    [Fact]
    public void Single_elimination_and_round_robin_ignore_segment_format_overrides()
    {
        var se = Tournament.Create(
            "Cup", Date, null, TournamentType.SingleElimination, MatchFormat.Bo3, ScoreType.Games, thirdPlaceEnabled: false, CreatedAt,
            upperBracketFormat: MatchFormat.Bo7, lowerBracketFormat: MatchFormat.Bo1, grandFinalFormat: MatchFormat.Bo5, groupStageMatchFormat: MatchFormat.Bo2);

        se.UpperBracketFormat.Should().Be(MatchFormat.Bo3);
        se.LowerBracketFormat.Should().Be(MatchFormat.Bo3);
        se.GrandFinalFormat.Should().Be(MatchFormat.Bo3);
        se.GroupStageMatchFormat.Should().Be(MatchFormat.Bo3);

        var rr = Tournament.Create(
            "League", Date, null, TournamentType.RoundRobin, MatchFormat.Bo1, ScoreType.WinnerOnly, thirdPlaceEnabled: false, CreatedAt,
            upperBracketFormat: MatchFormat.Bo7, lowerBracketFormat: MatchFormat.Bo5, grandFinalFormat: MatchFormat.Bo5, groupStageMatchFormat: MatchFormat.Bo2);

        rr.UpperBracketFormat.Should().Be(MatchFormat.Bo1);
        rr.LowerBracketFormat.Should().Be(MatchFormat.Bo1);
        rr.GrandFinalFormat.Should().Be(MatchFormat.Bo1);
        rr.GroupStageMatchFormat.Should().Be(MatchFormat.Bo1);
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
