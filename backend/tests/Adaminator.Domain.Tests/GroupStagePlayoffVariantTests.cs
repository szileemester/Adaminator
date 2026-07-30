using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

/// <summary>
/// The Group Stage + Playoff variant matrix: {round-robin groups, Swiss} x {single, double elimination}.
/// <see cref="GroupStagePlayoffMatchResultTests"/> already covers the round-robin + double-elimination
/// combination in depth, so these focus on the three new ones and on what the two new settings change.
/// </summary>
public class GroupStagePlayoffVariantTests
{
    private static readonly DateOnly Date = new(2026, 7, 26);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    // ---- MinParticipantsToStart ----
    //
    // The roster editor floors its player count here, so this has to agree with what Start actually
    // rejects. Each case below builds the smallest roster the property allows and asserts it starts,
    // then one fewer and asserts it doesn't - a floor that is too low would let an admin build a
    // roster the tournament refuses, and one too high would block a legal one.

    [Theory]
    [InlineData(TournamentType.SingleElimination, 2)]
    [InlineData(TournamentType.DoubleElimination, 2)]
    [InlineData(TournamentType.RoundRobin, 2)]
    public void A_plain_type_can_start_with_two(TournamentType type, int expected)
    {
        var tournament = Tournament.Create(
            "Cup", Date, null, type, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt);

        tournament.MinParticipantsToStart.Should().Be(expected);
    }

    [Theory]
    // groups x 2, floored at the smallest playoff
    [InlineData(2, 0, 0, GroupStageKind.RoundRobin, 4)]
    [InlineData(4, 0, 0, GroupStageKind.RoundRobin, 8)]
    [InlineData(6, 0, 0, GroupStageKind.RoundRobin, 12)]
    // an explicitly chosen playoff cut needs a roster that fills it
    [InlineData(2, 8, 0, GroupStageKind.RoundRobin, 8)]
    [InlineData(2, 16, 0, GroupStageKind.RoundRobin, 16)]
    // Swiss has no groups; only the cut and the round count raise the floor
    [InlineData(0, 0, 0, GroupStageKind.Swiss, 4)]
    [InlineData(0, 16, 0, GroupStageKind.Swiss, 16)]
    [InlineData(0, 0, 6, GroupStageKind.Swiss, 7)]
    public void Group_stage_playoff_reports_the_highest_floor_its_settings_impose(
        int groupCount, int playoffSize, int swissRounds, GroupStageKind kind, int expected)
    {
        var tournament = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt,
            groupCount: groupCount, groupStageKind: kind, playoffSize: playoffSize, swissRounds: swissRounds);

        tournament.MinParticipantsToStart.Should().Be(expected);
    }

    /// <summary>
    /// The property's whole purpose: a roster of exactly this size must start. The frontend's previous
    /// hand-written mirror said 4 here and let an admin build a roster Start then refused.
    /// </summary>
    [Fact]
    public void A_roster_of_exactly_the_minimum_starts_even_with_an_explicit_playoff_cut()
    {
        var tournament = Planned(participantCount: 16, playoffSize: 16);
        tournament.DrawGroups();

        tournament.MinParticipantsToStart.Should().Be(16);
        tournament.Invoking(t => t.Start()).Should().NotThrow();
    }

    [Fact]
    public void One_below_the_reported_minimum_is_refused_by_Start()
    {
        var tournament = Planned(participantCount: 15, playoffSize: 16);
        tournament.DrawGroups();

        tournament.Invoking(t => t.Start())
            .Should().Throw<DomainException>().WithMessage("*playoff of 16 needs at least 16*");
    }

    private static Tournament Planned(
        int participantCount,
        GroupStageKind groupStageKind = GroupStageKind.RoundRobin,
        PlayoffKind playoffKind = PlayoffKind.DoubleElimination,
        int groupCount = 2,
        int playoffSize = 0,
        int swissRounds = 0,
        bool thirdPlaceEnabled = false)
    {
        var tournament = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.Games,
            thirdPlaceEnabled, CreatedAt,
            groupCount: groupStageKind == GroupStageKind.Swiss ? 0 : groupCount,
            groupStageKind: groupStageKind,
            playoffKind: playoffKind,
            playoffSize: playoffSize,
            swissRounds: swissRounds);

        for (var i = 1; i <= participantCount; i++)
        {
            tournament.AddParticipant($"P{i:00}");
        }

        return tournament;
    }

    /// <summary>Seeds the roster in roster order with no byes - the pre-start step a Swiss pool needs.</summary>
    private static void Seed(Tournament tournament) =>
        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());

    private static Tournament StartedSwiss(
        int participantCount, PlayoffKind playoffKind = PlayoffKind.DoubleElimination, int playoffSize = 0, int swissRounds = 0)
    {
        var tournament = Planned(participantCount, GroupStageKind.Swiss, playoffKind, playoffSize: playoffSize, swissRounds: swissRounds);
        Seed(tournament);
        tournament.Start();
        return tournament;
    }

    private static Tournament StartedGroups(
        int participantCount, PlayoffKind playoffKind, int groupCount = 2, int playoffSize = 0, bool thirdPlaceEnabled = false)
    {
        var tournament = Planned(participantCount, GroupStageKind.RoundRobin, playoffKind, groupCount, playoffSize, thirdPlaceEnabled: thirdPlaceEnabled);
        tournament.DrawGroups();
        tournament.Start();
        return tournament;
    }

    private static void CompleteA(Tournament tournament, Match match) =>
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, true) }, Now);

    /// <summary>Decides every pending group-stage match so the lower seed always wins, leaving a strict order.</summary>
    private static void DecideCurrentGroupStageRound(Tournament tournament)
    {
        foreach (var match in tournament.Matches
                     .Where(m => m.Segment == BracketSegment.RoundRobin && m.Status == MatchStatus.Pending)
                     .ToList())
        {
            var seedA = tournament.Participants.First(p => p.Id == match.ParticipantAId).Seed;
            var seedB = tournament.Participants.First(p => p.Id == match.ParticipantBId).Seed;
            tournament.CompleteMatch(
                match.Id, match.MatchFormat, ScoreType.Games, new List<ScoreEntryInput> { new(null, null, seedA < seedB) }, Now);
        }
    }

    private static void PlayOutSwissGroupStage(Tournament tournament)
    {
        DecideCurrentGroupStageRound(tournament);
        while (tournament.CanStartNextSwissRound)
        {
            tournament.StartNextSwissRound();
            DecideCurrentGroupStageRound(tournament);
        }
    }

    private static void PlayOutPlayoff(Tournament tournament)
    {
        for (var guard = 0; guard < 500; guard++)
        {
            var next = tournament.Matches.FirstOrDefault(m =>
                m.Segment is BracketSegment.Winner or BracketSegment.Loser or BracketSegment.GrandFinal or BracketSegment.ThirdPlace
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

    // ---- The matrix ----

    [Theory]
    [InlineData(GroupStageKind.RoundRobin, PlayoffKind.DoubleElimination)]
    [InlineData(GroupStageKind.RoundRobin, PlayoffKind.SingleElimination)]
    [InlineData(GroupStageKind.Swiss, PlayoffKind.DoubleElimination)]
    [InlineData(GroupStageKind.Swiss, PlayoffKind.SingleElimination)]
    public void Every_variant_plays_from_the_group_stage_through_to_a_finishable_playoff(
        GroupStageKind groupStageKind, PlayoffKind playoffKind)
    {
        var tournament = groupStageKind == GroupStageKind.Swiss
            ? StartedSwiss(8, playoffKind)
            : StartedGroups(8, playoffKind);

        if (groupStageKind == GroupStageKind.Swiss)
        {
            PlayOutSwissGroupStage(tournament);
        }
        else
        {
            DecideCurrentGroupStageRound(tournament);
        }

        tournament.CanStartPlayoffs.Should().BeTrue();
        tournament.StartPlayoffs();
        PlayOutPlayoff(tournament);

        tournament.CanFinish.Should().BeTrue();
        tournament.Finish();
        tournament.Status.Should().Be(TournamentStatus.Finished);
    }

    // ---- Single elimination playoff ----

    [Fact]
    public void A_single_elimination_playoff_seeds_every_qualifier_into_one_bracket()
    {
        var tournament = StartedGroups(8, PlayoffKind.SingleElimination);
        DecideCurrentGroupStageRound(tournament);
        tournament.StartPlayoffs();

        var playoff = tournament.Matches.Where(m => m.Segment != BracketSegment.RoundRobin).ToList();
        playoff.Should().OnlyContain(m => m.Segment == BracketSegment.Winner);
        // A bye-free tree over 8 qualifiers: 4 + 2 + 1.
        playoff.Should().HaveCount(7);
        playoff.Count(m => m.Round == 1).Should().Be(4);
        playoff.Where(m => m.Round == 1).Should().OnlyContain(m => m.ParticipantAId != null && m.ParticipantBId != null);
        playoff.Where(m => m.Round > 1).Should().OnlyContain(m => m.ParticipantAId == null && m.ParticipantBId == null);
    }

    [Fact]
    public void A_single_elimination_playoff_stores_its_forward_routes()
    {
        var tournament = StartedGroups(8, PlayoffKind.SingleElimination);
        DecideCurrentGroupStageRound(tournament);
        tournament.StartPlayoffs();

        var winner = tournament.Matches.Where(m => m.Segment == BracketSegment.Winner).ToList();
        var finalRound = winner.Max(m => m.Round);

        // Every match but the Final routes its winner onward, so advancement never falls back to
        // roster-derived round math.
        winner.Where(m => m.Round < finalRound).Should().OnlyContain(m => m.WinnerToMatchId != null);
        winner.Single(m => m.Round == finalRound).WinnerToMatchId.Should().BeNull();
    }

    [Fact]
    public void A_single_elimination_playoff_can_carry_a_third_place_match()
    {
        var tournament = StartedGroups(8, PlayoffKind.SingleElimination, thirdPlaceEnabled: true);
        DecideCurrentGroupStageRound(tournament);
        tournament.StartPlayoffs();

        var thirdPlace = tournament.Matches.Single(m => m.Segment == BracketSegment.ThirdPlace);
        var semifinalRound = tournament.Matches.Where(m => m.Segment == BracketSegment.Winner).Max(m => m.Round) - 1;
        var semifinals = tournament.Matches.Where(m => m.Segment == BracketSegment.Winner && m.Round == semifinalRound).ToList();

        semifinals.Should().HaveCount(2);
        semifinals.Should().OnlyContain(m => m.LoserToMatchId == thirdPlace.Id);
    }

    [Fact]
    public void A_single_elimination_playoff_is_not_finished_until_its_third_place_match_is_decided()
    {
        var tournament = StartedGroups(8, PlayoffKind.SingleElimination, thirdPlaceEnabled: true);
        DecideCurrentGroupStageRound(tournament);
        tournament.StartPlayoffs();

        // Play only the winner bracket, leaving Third Place open.
        for (var guard = 0; guard < 500; guard++)
        {
            var next = tournament.Matches.FirstOrDefault(m =>
                m.Segment == BracketSegment.Winner && m.Status == MatchStatus.Pending
                && m.ParticipantAId is not null && m.ParticipantBId is not null);
            if (next is null)
            {
                break;
            }

            CompleteA(tournament, next);
        }

        tournament.CanFinish.Should().BeFalse();

        var thirdPlace = tournament.Matches.Single(m => m.Segment == BracketSegment.ThirdPlace);
        CompleteA(tournament, thirdPlace);
        tournament.CanFinish.Should().BeTrue();
    }

    [Fact]
    public void Undoing_a_single_elimination_playoff_match_clears_the_slot_it_advanced_into()
    {
        var tournament = StartedGroups(8, PlayoffKind.SingleElimination);
        DecideCurrentGroupStageRound(tournament);
        tournament.StartPlayoffs();

        var first = tournament.Matches.First(m => m.Segment == BracketSegment.Winner && m.Round == 1);
        CompleteA(tournament, first);

        var next = tournament.Matches.Single(m => m.Id == first.WinnerToMatchId);
        (next.ParticipantAId ?? next.ParticipantBId).Should().NotBeNull();

        tournament.CanUndo(first.Id).Should().BeTrue();
        tournament.UndoMatch(first.Id);

        next.ParticipantAId.Should().BeNull();
        next.ParticipantBId.Should().BeNull();
    }

    [Fact]
    public void Third_place_is_available_for_a_single_elimination_playoff_but_not_a_double_one()
    {
        var single = Planned(8, playoffKind: PlayoffKind.SingleElimination, thirdPlaceEnabled: true);
        single.ThirdPlaceEnabled.Should().BeTrue();

        var act = () => Planned(8, playoffKind: PlayoffKind.DoubleElimination, thirdPlaceEnabled: true);
        act.Should().Throw<DomainException>().WithMessage("*Single Elimination*");
    }

    // ---- The admin-chosen playoff cut ----

    [Fact]
    public void An_unset_playoff_size_still_takes_the_largest_capacity_the_roster_fills()
    {
        Planned(9).PlayoffCapacity.Should().Be(8);
        Planned(16).PlayoffCapacity.Should().Be(16);
    }

    [Fact]
    public void A_chosen_playoff_size_cuts_the_field_even_when_the_roster_could_fill_more()
    {
        var tournament = StartedGroups(16, PlayoffKind.SingleElimination, groupCount: 2, playoffSize: 4);
        DecideCurrentGroupStageRound(tournament);
        tournament.StartPlayoffs();

        tournament.PlayoffCapacity.Should().Be(4);
        var seeded = tournament.Matches
            .Where(m => m.Segment == BracketSegment.Winner && m.Round == 1)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
            .Where(id => id is not null)
            .ToList();
        seeded.Should().HaveCount(4);
    }

    [Fact]
    public void A_playoff_larger_than_the_roster_is_rejected_at_start()
    {
        var tournament = Planned(8, playoffSize: 16);
        tournament.DrawGroups();

        var act = tournament.Start;
        act.Should().Throw<DomainException>().WithMessage("*at least 16 participants*");
    }

    [Fact]
    public void An_unsupported_playoff_size_is_rejected_outright()
    {
        var act = () => Planned(8, playoffSize: 6);
        act.Should().Throw<DomainException>().WithMessage("*not supported*");
    }

    // ---- Swiss lifecycle ----

    [Fact]
    public void A_swiss_group_stage_starts_with_only_its_first_round()
    {
        var tournament = StartedSwiss(8);

        tournament.Matches.Should().OnlyContain(m => m.Segment == BracketSegment.RoundRobin && m.Round == 1);
        tournament.Matches.Should().HaveCount(4);
    }

    [Fact]
    public void A_swiss_group_stage_has_no_groups_to_draw()
    {
        var tournament = Planned(8, GroupStageKind.Swiss);

        var act = tournament.DrawGroups;
        act.Should().Throw<DomainException>().WithMessage("*single pool*");
    }

    [Fact]
    public void Round_robin_groups_have_no_swiss_round_to_start()
    {
        var tournament = StartedGroups(8, PlayoffKind.DoubleElimination);

        var act = tournament.StartNextSwissRound;
        act.Should().Throw<DomainException>().WithMessage("*Swiss group stage*");
    }

    [Fact]
    public void The_next_swiss_round_cannot_be_paired_until_the_current_one_is_decided()
    {
        var tournament = StartedSwiss(8);

        tournament.CanStartNextSwissRound.Should().BeFalse();
        var act = tournament.StartNextSwissRound;
        act.Should().Throw<DomainException>().WithMessage("*must be decided*");

        DecideCurrentGroupStageRound(tournament);
        tournament.CanStartNextSwissRound.Should().BeTrue();
    }

    [Fact]
    public void A_swiss_playoff_stays_blocked_until_every_scheduled_round_has_been_played()
    {
        // 8 players defaults to ceil(log2 8) = 3 rounds.
        var tournament = StartedSwiss(8);
        tournament.ResolvedSwissRounds.Should().Be(3);

        DecideCurrentGroupStageRound(tournament);
        tournament.CanStartPlayoffs.Should().BeFalse("round 1 being decided does not end the pool");

        tournament.StartNextSwissRound();
        DecideCurrentGroupStageRound(tournament);
        tournament.CanStartPlayoffs.Should().BeFalse();

        tournament.StartNextSwissRound();
        DecideCurrentGroupStageRound(tournament);
        tournament.CanStartNextSwissRound.Should().BeFalse();
        tournament.CanStartPlayoffs.Should().BeTrue();
    }

    [Fact]
    public void A_swiss_round_count_can_be_chosen_instead_of_the_default()
    {
        // A single-elimination playoff of 8 takes the whole field, so no placement straddles a cut and
        // the pool ending is the only thing gating the playoff.
        var tournament = StartedSwiss(8, PlayoffKind.SingleElimination, swissRounds: 2);
        tournament.ResolvedSwissRounds.Should().Be(2);

        DecideCurrentGroupStageRound(tournament);
        tournament.StartNextSwissRound();
        DecideCurrentGroupStageRound(tournament);

        tournament.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).Max(m => m.Round).Should().Be(2);
        tournament.CanStartNextSwissRound.Should().BeFalse();
        tournament.CanStartPlayoffs.Should().BeTrue();
    }

    [Fact]
    public void A_swiss_group_stage_rejects_best_of_two()
    {
        var act = () => Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.Games, false, CreatedAt,
            groupCount: 0, groupStageMatchFormat: MatchFormat.Bo2, groupStageKind: GroupStageKind.Swiss);

        act.Should().Throw<DomainException>().WithMessage("*Best of 2*");
    }

    [Fact]
    public void A_swiss_pool_seeds_the_playoff_straight_from_its_single_standings_table()
    {
        var tournament = StartedSwiss(8, PlayoffKind.SingleElimination);
        PlayOutSwissGroupStage(tournament);
        tournament.StartPlayoffs();

        var seeded = tournament.Matches
            .Where(m => m.Segment == BracketSegment.Winner && m.Round == 1)
            .SelectMany(m => new[] { m.ParticipantAId!.Value, m.ParticipantBId!.Value })
            .ToList();

        seeded.Should().HaveCount(8).And.OnlyHaveUniqueItems();
    }
}
