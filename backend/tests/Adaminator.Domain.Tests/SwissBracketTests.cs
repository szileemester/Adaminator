using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

/// <summary>Swiss pairing itself: round counts, who meets whom, and how an odd roster is handled.</summary>
public class SwissBracketTests
{
    private static readonly DateOnly Date = new(2026, 7, 26);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static Tournament StartedSwiss(int participantCount, int swissRounds = 0)
    {
        var tournament = Tournament.Create(
            "Swiss Open", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt,
            groupCount: 0, groupStageKind: GroupStageKind.Swiss, swissRounds: swissRounds);

        for (var i = 1; i <= participantCount; i++)
        {
            tournament.AddParticipant($"P{i:00}");
        }

        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());
        tournament.Start();
        return tournament;
    }

    /// <summary>Decides every pending Swiss match so participant A always wins - a deterministic, tie-producing pattern.</summary>
    private static void DecideRound(Tournament tournament)
    {
        foreach (var match in tournament.Matches
                     .Where(m => m.Segment == BracketSegment.RoundRobin && m.Status == MatchStatus.Pending)
                     .ToList())
        {
            tournament.CompleteMatch(
                match.Id, match.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) }, Now);
        }
    }

    private static List<Match> Round(Tournament tournament, int round) =>
        tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin && m.Round == round).ToList();

    // ---- Round counts ----

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(8, 3)]
    [InlineData(9, 4)]
    [InlineData(16, 4)]
    [InlineData(32, 5)]
    public void DefaultRounds_is_enough_to_separate_one_undefeated_leader(int participants, int expected)
    {
        SwissBracket.DefaultRounds(participants).Should().Be(expected);
    }

    [Fact]
    public void ValidateShape_rejects_more_rounds_than_a_full_round_robin_would_play()
    {
        var act = () => SwissBracket.ValidateShape(4, 5);
        act.Should().Throw<DomainException>().WithMessage("*at most 3*");
    }

    [Fact]
    public void ValidateShape_rejects_a_pool_too_small_to_pair()
    {
        var act = () => SwissBracket.ValidateShape(1, 1);
        act.Should().Throw<DomainException>().WithMessage("*at least 2 participants*");
    }

    // ---- Pairing ----

    [Fact]
    public void Round_one_pairs_the_seed_order_adjacently()
    {
        var tournament = StartedSwiss(8);
        var seeds = tournament.Participants.OrderBy(p => p.Seed).Select(p => p.Id).ToList();

        var round1 = Round(tournament, 1).OrderBy(m => m.IndexInRound).ToList();
        round1.Should().HaveCount(4);
        for (var i = 0; i < round1.Count; i++)
        {
            round1[i].ParticipantAId.Should().Be(seeds[2 * i]);
            round1[i].ParticipantBId.Should().Be(seeds[2 * i + 1]);
        }
    }

    [Fact]
    public void Nobody_meets_the_same_opponent_twice_across_a_full_run()
    {
        var tournament = StartedSwiss(8);
        DecideRound(tournament);

        while (tournament.CanStartNextSwissRound)
        {
            tournament.StartNextSwissRound();
            DecideRound(tournament);
        }

        var meetings = tournament.Matches
            .Where(m => m.Segment == BracketSegment.RoundRobin && m.ParticipantBId is not null)
            .Select(m => m.ParticipantAId!.Value.CompareTo(m.ParticipantBId!.Value) <= 0
                ? (m.ParticipantAId!.Value, m.ParticipantBId!.Value)
                : (m.ParticipantBId!.Value, m.ParticipantAId!.Value))
            .ToList();

        meetings.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Equal_records_meet_each_other_in_the_second_round()
    {
        var tournament = StartedSwiss(8);
        DecideRound(tournament);
        tournament.StartNextSwissRound();

        // Participant A always won, so round 1 produced four 1-0s and four 0-1s. Round 2 should pair
        // winners with winners and losers with losers.
        var winners = Round(tournament, 1).Select(m => m.WinnerId!.Value).ToHashSet();

        foreach (var match in Round(tournament, 2))
        {
            var aWon = winners.Contains(match.ParticipantAId!.Value);
            var bWon = winners.Contains(match.ParticipantBId!.Value);
            aWon.Should().Be(bWon, "a Swiss round pairs participants on equal records");
        }
    }

    // ---- Byes ----

    [Fact]
    public void An_odd_pool_gives_exactly_one_bye_per_round()
    {
        var tournament = StartedSwiss(7);

        var round1 = Round(tournament, 1);
        round1.Count(m => m.IsBye).Should().Be(1);
        round1.Count(m => !m.IsBye).Should().Be(3);
    }

    [Fact]
    public void A_bye_is_already_decided_in_favour_of_the_participant_who_sat_out()
    {
        var tournament = StartedSwiss(7);
        var bye = Round(tournament, 1).Single(m => m.IsBye);

        bye.IsDecided.Should().BeTrue();
        bye.WinnerId.Should().Be(bye.ParticipantAId);
        bye.ParticipantBId.Should().BeNull();
        bye.LoserId.Should().BeNull();
    }

    [Fact]
    public void A_bye_counts_as_a_win_in_the_standings()
    {
        var tournament = StartedSwiss(7);
        var byeRecipient = Round(tournament, 1).Single(m => m.IsBye).ParticipantAId!.Value;
        DecideRound(tournament);

        var standings = RoundRobinStandings.Rank(
            tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin),
            tournament.Participants.ToList(),
            tournament.Participants.ToDictionary(p => p.Id));

        var row = standings.Single(r => r.ParticipantId == byeRecipient);
        row.Wins.Should().Be(1);
        row.Played.Should().Be(1);
    }

    [Fact]
    public void The_bye_goes_to_the_lowest_ranked_participant_who_has_not_had_one()
    {
        var tournament = StartedSwiss(7);
        var firstBye = Round(tournament, 1).Single(m => m.IsBye).ParticipantAId!.Value;

        // The lowest seed sits out first, since round 1 ranks by seed.
        var lowestSeed = tournament.Participants.OrderByDescending(p => p.Seed).First().Id;
        firstBye.Should().Be(lowestSeed);

        DecideRound(tournament);
        tournament.StartNextSwissRound();
        Round(tournament, 2).Single(m => m.IsBye).ParticipantAId.Should().NotBe(firstBye);
    }

    [Fact]
    public void Nobody_takes_a_second_bye_before_everyone_has_had_one()
    {
        // 5 participants over 2 rounds: two different players must sit out.
        var tournament = StartedSwiss(5, swissRounds: 2);
        DecideRound(tournament);
        tournament.StartNextSwissRound();

        var recipients = tournament.Matches.Where(m => m.IsBye).Select(m => m.ParticipantAId!.Value).ToList();
        recipients.Should().HaveCount(2).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_bye_can_never_be_undone()
    {
        var tournament = StartedSwiss(7);
        var bye = Round(tournament, 1).Single(m => m.IsBye);

        tournament.CanUndo(bye.Id).Should().BeFalse();
        var act = () => tournament.UndoMatch(bye.Id);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_real_result_can_still_be_undone_in_a_round_that_contains_a_bye()
    {
        var tournament = StartedSwiss(7);
        var played = Round(tournament, 1).First(m => !m.IsBye);
        tournament.CompleteMatch(
            played.Id, played.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) }, Now);

        tournament.CanUndo(played.Id).Should().BeTrue();
    }
}
