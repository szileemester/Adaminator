using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class GroupStagePlayoffMatchResultTests
{
    private static readonly DateOnly Date = new(2026, 7, 18);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private static Tournament Planned(int participantCount, int groupCount)
    {
        var tournament = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.WinnerOnly, false, CreatedAt, groupCount);
        for (var i = 1; i <= participantCount; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        return tournament;
    }

    private static Tournament StartedGroupStage(int participantCount, int groupCount)
    {
        var tournament = Planned(participantCount, groupCount);
        tournament.DrawGroups();
        tournament.Start();
        return tournament;
    }

    private static void CompleteA(Tournament tournament, Match match) =>
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, true) }, Now);

    /// <summary>Completes a group match with the stronger (lower within-group seed) participant winning, so each group ends in a strict, tie-free order.</summary>
    private static void CompleteSeeded(Tournament tournament, Match match)
    {
        var seedA = tournament.Participants.First(p => p.Id == match.ParticipantAId).Seed;
        var seedB = tournament.Participants.First(p => p.Id == match.ParticipantBId).Seed;
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, seedA < seedB) }, Now);
    }

    private static void DecideAllGroupMatches(Tournament tournament)
    {
        foreach (var match in tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList())
        {
            CompleteSeeded(tournament, match);
        }
    }

    private static void PlayOutPlayoff(Tournament tournament)
    {
        for (var guard = 0; guard < 500; guard++)
        {
            var next = tournament.Matches.FirstOrDefault(m =>
                m.Segment is BracketSegment.Winner or BracketSegment.Loser or BracketSegment.GrandFinal
                && m.Status == MatchStatus.Pending
                && m.ParticipantAId is not null
                && m.ParticipantBId is not null);
            if (next is null)
            {
                return;
            }

            CompleteA(tournament, next);
        }

        throw new InvalidOperationException("Playoff did not resolve within the iteration guard.");
    }

    // ---- Group draw + start ----

    [Fact]
    public void DrawGroups_assigns_a_balanced_group_to_every_participant()
    {
        var tournament = Planned(8, 2);

        tournament.DrawGroups();

        tournament.Participants.Should().OnlyContain(p => p.GroupIndex == 0 || p.GroupIndex == 1);
        tournament.Participants.Count(p => p.GroupIndex == 0).Should().Be(4);
        tournament.Participants.Count(p => p.GroupIndex == 1).Should().Be(4);
    }

    [Fact]
    public void DrawGroups_rejects_a_roster_too_small_for_a_playoff()
    {
        var tournament = Planned(3, 2); // fewer than the smallest playoff capacity

        tournament.Invoking(t => t.DrawGroups()).Should().Throw<DomainException>();
    }

    [Fact]
    public void DrawGroups_rejects_more_groups_than_the_roster_can_fill()
    {
        var tournament = Planned(8, 8); // groups of one have no matches

        tournament.Invoking(t => t.DrawGroups()).Should().Throw<DomainException>();
    }

    [Fact]
    public void DrawGroups_deals_uneven_groups_when_the_roster_does_not_divide()
    {
        var tournament = Planned(9, 2);

        tournament.DrawGroups();

        tournament.Participants.Count(p => p.GroupIndex == 0).Should().Be(5);
        tournament.Participants.Count(p => p.GroupIndex == 1).Should().Be(4);
        tournament.Participants.Should().OnlyContain(p => p.GroupIndex != null && p.Seed > 0);
    }

    [Fact]
    public void Start_builds_one_round_robin_per_group_and_no_playoff_yet()
    {
        var tournament = StartedGroupStage(8, 2); // two groups of four

        var groupMatches = tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList();
        groupMatches.Should().HaveCount(12); // C(4,2) = 6 per group
        groupMatches.Count(m => m.GroupIndex == 0).Should().Be(6);
        groupMatches.Count(m => m.GroupIndex == 1).Should().Be(6);
        tournament.Matches.Should().OnlyContain(m => m.Segment == BracketSegment.RoundRobin);
    }

    [Fact]
    public void Start_before_drawing_groups_is_rejected()
    {
        var tournament = Planned(8, 2);

        tournament.Invoking(t => t.Start()).Should().Throw<DomainException>().WithMessage("*Draw the groups*");
    }

    [Fact]
    public void Completing_a_group_match_advances_nothing()
    {
        var tournament = StartedGroupStage(8, 2);
        var target = tournament.Matches.First();

        CompleteA(tournament, target);

        tournament.Matches.Where(m => m.Id != target.Id).Should().OnlyContain(m => m.Status == MatchStatus.Pending);
    }

    // ---- Start playoffs ----

    [Fact]
    public void StartPlayoffs_is_rejected_until_every_group_match_is_decided()
    {
        var tournament = StartedGroupStage(8, 2);
        tournament.CanStartPlayoffs.Should().BeFalse();
        tournament.Invoking(t => t.StartPlayoffs()).Should().Throw<DomainException>();

        var groupMatches = tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList();
        foreach (var match in groupMatches.Skip(1))
        {
            CompleteSeeded(tournament, match);
        }

        tournament.CanStartPlayoffs.Should().BeFalse();

        CompleteSeeded(tournament, groupMatches[0]);
        tournament.CanStartPlayoffs.Should().BeTrue();
        tournament.Invoking(t => t.StartPlayoffs()).Should().NotThrow();
    }

    [Fact]
    public void StartPlayoffs_seeds_each_group_top_half_into_the_winner_bracket_and_bottom_half_into_the_loser_bracket()
    {
        var tournament = StartedGroupStage(8, 2);
        DecideAllGroupMatches(tournament);

        tournament.StartPlayoffs();

        var roster = tournament.Participants.ToDictionary(p => p.Id);
        var expectedUpper = new HashSet<Guid>();
        for (var g = 0; g < 2; g++)
        {
            var participants = tournament.Participants.Where(p => p.GroupIndex == g).ToList();
            var groupMatches = tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin && m.GroupIndex == g);
            var ranked = RoundRobinStandings.Rank(groupMatches, participants, roster);
            expectedUpper.Add(ranked[0].ParticipantId);
            expectedUpper.Add(ranked[1].ParticipantId);
        }

        var upperInPlayoff = tournament.Matches
            .Where(m => m.Segment == BracketSegment.Winner && m.Round == 2)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        var lowerInPlayoff = tournament.Matches
            .Where(m => m.Segment == BracketSegment.Loser && m.Round == 1)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        upperInPlayoff.Should().BeEquivalentTo(expectedUpper);
        lowerInPlayoff.Should().HaveCount(4);
        upperInPlayoff.Should().NotIntersectWith(lowerInPlayoff);
    }

    // ---- Finish ----

    [Fact]
    public void Cannot_finish_until_the_playoff_grand_final_is_decided()
    {
        var tournament = StartedGroupStage(8, 2);
        DecideAllGroupMatches(tournament);
        tournament.CanFinish.Should().BeFalse(); // group stage done, playoff not started

        tournament.StartPlayoffs();
        tournament.CanFinish.Should().BeFalse(); // playoff just started

        PlayOutPlayoff(tournament);
        tournament.CanFinish.Should().BeTrue();

        tournament.Finish();
        tournament.Status.Should().Be(TournamentStatus.Finished);
    }

    // ---- Per-stage undo ----

    [Fact]
    public void A_decided_group_match_can_be_undone_during_the_group_stage()
    {
        var tournament = StartedGroupStage(8, 2);
        var groupMatch = tournament.Matches.First(m => m.Segment == BracketSegment.RoundRobin);
        CompleteA(tournament, groupMatch);

        tournament.CanUndo(groupMatch.Id).Should().BeTrue();
        tournament.UndoMatch(groupMatch.Id);

        groupMatch.WinnerId.Should().BeNull();
    }

    [Fact]
    public void Undoing_a_playoff_match_clears_the_advanced_slot()
    {
        var tournament = StartedGroupStage(8, 2);
        DecideAllGroupMatches(tournament);
        tournament.StartPlayoffs();

        var winnerRoundTwo = tournament.Matches.First(m => m.Segment == BracketSegment.Winner && m.Round == 2);
        CompleteA(tournament, winnerRoundTwo);

        tournament.CanUndo(winnerRoundTwo.Id).Should().BeTrue();
        tournament.UndoMatch(winnerRoundTwo.Id);

        winnerRoundTwo.WinnerId.Should().BeNull();
    }
}
