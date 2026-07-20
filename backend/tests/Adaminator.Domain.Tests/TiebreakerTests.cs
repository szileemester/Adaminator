using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class TiebreakerTests
{
    private static readonly DateOnly Date = new(2026, 7, 19);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    // ---- Round Robin helpers ----

    private static Tournament StartedRoundRobin(
        int participants, TiebreakerPolicy policy, MatchFormat format = MatchFormat.Bo1, ScoreType scoreType = ScoreType.WinnerOnly)
    {
        var tournament = Tournament.Create(
            "League", Date, null, TournamentType.RoundRobin, format, scoreType, false, CreatedAt, tiebreakerPolicy: policy);
        for (var i = 1; i <= participants; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        tournament.ApplySeeding(tournament.Participants.Select(p => p.Id).ToList(), Array.Empty<Guid>());
        tournament.Start();
        return tournament;
    }

    private static Match RoundRobinMatch(Tournament tournament, string a, string b, BracketSegment segment = BracketSegment.RoundRobin)
    {
        var idA = tournament.Participants.First(p => p.Name == a).Id;
        var idB = tournament.Participants.First(p => p.Name == b).Id;
        return tournament.Matches.First(m =>
            m.Segment == segment
            && ((m.ParticipantAId == idA && m.ParticipantBId == idB) || (m.ParticipantAId == idB && m.ParticipantBId == idA)));
    }

    /// <summary>Completes the round-robin match between two participants (WinnerOnly, Bo1) with <paramref name="winner"/> winning.</summary>
    private static void Win(Tournament tournament, string winner, string loser)
    {
        var match = RoundRobinMatch(tournament, winner, loser);
        var winnerIsA = match.ParticipantAId == tournament.Participants.First(p => p.Name == winner).Id;
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, winnerIsA) }, Now);
    }

    /// <summary>Completes a Games-scored match with <paramref name="winner"/> taking <paramref name="winnerGames"/>-<paramref name="loserGames"/>, so a game differential exists.</summary>
    private static void WinGames(Tournament tournament, string winner, string loser, int winnerGames, int loserGames)
    {
        var match = RoundRobinMatch(tournament, winner, loser);
        var winnerIsA = match.ParticipantAId == tournament.Participants.First(p => p.Name == winner).Id;
        var entries = new List<ScoreEntryInput>();
        for (var i = 0; i < winnerGames; i++)
        {
            entries.Add(new(null, null, winnerIsA));
        }

        for (var i = 0; i < loserGames; i++)
        {
            entries.Add(new(null, null, !winnerIsA));
        }

        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.Games, entries, Now);
    }

    private static List<string> RankedNames(Tournament tournament)
    {
        var roster = tournament.Participants.ToDictionary(p => p.Id);
        var scope = tournament.Matches.Where(m => m.Segment is BracketSegment.RoundRobin or BracketSegment.Tiebreaker);
        return RoundRobinStandings.Rank(scope, tournament.Participants, roster).Select(r => roster[r.ParticipantId].Name).ToList();
    }

    // ---- Computed criteria ----

    [Fact]
    public void Head_to_head_orders_a_two_way_tie_and_needs_no_match()
    {
        // P1 & P2 both finish 2-1; P1 beat P2. P3 & P4 both finish 1-2; P3 beat P4.
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P4", "P1");
        Win(t, "P2", "P3");
        Win(t, "P2", "P4");
        Win(t, "P3", "P4");

        RankedNames(t).Should().Equal("P1", "P2", "P3", "P4");
        t.NeedsTiebreakers.Should().BeFalse();
    }

    [Fact]
    public void Game_differential_breaks_a_head_to_head_cycle_without_a_match()
    {
        // Three-way cycle on match record; game differentials P1 > P2 > P3 settle it.
        var t = StartedRoundRobin(3, TiebreakerPolicy.ComputedThenMatch, MatchFormat.Bo3, ScoreType.Games);
        WinGames(t, "P1", "P2", 2, 0); // P1 +2 / P2 -2
        WinGames(t, "P2", "P3", 2, 0); // P2 +2 / P3 -2
        WinGames(t, "P3", "P1", 2, 1); // P3 +1 / P1 -1  => diffs P1 +1, P2 0, P3 -1

        RankedNames(t).Should().Equal("P1", "P2", "P3");
        t.NeedsTiebreakers.Should().BeFalse();
    }

    [Fact]
    public void A_cyclic_podium_deadlock_needs_a_tiebreaker_under_ComputedThenMatch()
    {
        // P1 sweeps; P2/P3/P4 form a rock-paper-scissors cycle at 1-2, no game scores to separate them.
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P1", "P4");
        Win(t, "P2", "P3");
        Win(t, "P3", "P4");
        Win(t, "P4", "P2");

        t.NeedsTiebreakers.Should().BeTrue();
        t.CanFinish.Should().BeFalse();
        t.Invoking(x => x.Finish()).Should().Throw<DomainException>();
    }

    [Fact]
    public void AlwaysMatch_plays_a_two_way_tie_that_head_to_head_would_have_resolved()
    {
        // P1 & P2 tie for first at 2-1; P1 beat P2 head-to-head. ComputedThenMatch would stop here.
        Tournament Build(TiebreakerPolicy policy)
        {
            var t = StartedRoundRobin(4, policy);
            Win(t, "P1", "P2");
            Win(t, "P1", "P3");
            Win(t, "P4", "P1");
            Win(t, "P2", "P3");
            Win(t, "P2", "P4");
            Win(t, "P3", "P4");
            return t;
        }

        Build(TiebreakerPolicy.ComputedThenMatch).NeedsTiebreakers.Should().BeFalse();
        Build(TiebreakerPolicy.AlwaysMatch).NeedsTiebreakers.Should().BeTrue();
    }

    [Fact]
    public void A_tie_below_the_podium_does_not_block_finishing()
    {
        // P1/P2/P3 strictly ordered on the podium; P4/P5/P6 cycle for 4th-6th (below the podium cuts).
        var t = StartedRoundRobin(6, TiebreakerPolicy.ComputedThenMatch);
        foreach (var strong in new[] { "P1", "P2", "P3" })
        {
            foreach (var weak in new[] { "P4", "P5", "P6" })
            {
                Win(t, strong, weak);
            }
        }

        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P2", "P3");
        Win(t, "P4", "P5");
        Win(t, "P5", "P6");
        Win(t, "P6", "P4");

        t.NeedsTiebreakers.Should().BeFalse();
        t.CanFinish.Should().BeTrue();
    }

    // ---- Played tie-breaker stage (Round Robin) ----

    [Fact]
    public void StartTiebreakers_generates_a_mini_round_robin_over_the_tied_cohort()
    {
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P1", "P4");
        Win(t, "P2", "P3");
        Win(t, "P3", "P4");
        Win(t, "P4", "P2");

        t.StartTiebreakers();

        var tiebreakers = t.Matches.Where(m => m.Segment == BracketSegment.Tiebreaker).ToList();
        tiebreakers.Should().HaveCount(3); // C(3,2) among P2/P3/P4
        tiebreakers.Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo1);
        var cohort = new[] { "P2", "P3", "P4" }.Select(n => t.Participants.First(p => p.Name == n).Id).ToHashSet();
        tiebreakers.SelectMany(m => new[] { m.ParticipantAId!.Value, m.ParticipantBId!.Value }).Should().OnlyContain(id => cohort.Contains(id));

        t.NeedsTiebreakers.Should().BeFalse(); // one-shot: already generated
        t.CanFinish.Should().BeFalse();        // ...but not yet decided
    }

    [Fact]
    public void Playing_the_tiebreaker_matches_settles_the_order_and_unblocks_finishing()
    {
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P1", "P4");
        Win(t, "P2", "P3");
        Win(t, "P3", "P4");
        Win(t, "P4", "P2");

        t.StartTiebreakers();

        // P2 wins the mini-league (beats P3 and P4); P3 beats P4.
        Win(t, "P2", "P3", BracketSegment.Tiebreaker);
        Win(t, "P2", "P4", BracketSegment.Tiebreaker);
        Win(t, "P3", "P4", BracketSegment.Tiebreaker);

        RankedNames(t).Should().Equal("P1", "P2", "P3", "P4");
        t.CanFinish.Should().BeTrue();
    }

    [Fact]
    public void A_tiebreaker_round_that_itself_cycles_produces_another_round()
    {
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P1", "P4");
        Win(t, "P2", "P3");
        Win(t, "P3", "P4");
        Win(t, "P4", "P2");

        t.StartTiebreakers();

        // The tie-breaker mini-league cycles too - nobody is separated.
        Win(t, "P2", "P3", BracketSegment.Tiebreaker);
        Win(t, "P3", "P4", BracketSegment.Tiebreaker);
        Win(t, "P4", "P2", BracketSegment.Tiebreaker);

        t.NeedsTiebreakers.Should().BeTrue();   // still deadlocked -> play again
        t.CanFinish.Should().BeFalse();

        t.StartTiebreakers();
        var secondWave = t.Matches.Where(m => m.Segment == BracketSegment.Tiebreaker && m.Round > 3).ToList();
        secondWave.Should().HaveCount(3);       // a fresh mini-league, numbered after the first

        // This time the lower name always wins: P2 2-0, P3 1-1, P4 0-2 - a strict order.
        foreach (var match in secondWave)
        {
            var nameA = t.Participants.First(p => p.Id == match.ParticipantAId).Name;
            var nameB = t.Participants.First(p => p.Id == match.ParticipantBId).Name;
            t.CompleteMatch(
                match.Id, match.MatchFormat, ScoreType.WinnerOnly,
                new List<ScoreEntryInput> { new(null, null, string.CompareOrdinal(nameA, nameB) < 0) }, Now);
        }

        t.NeedsTiebreakers.Should().BeFalse();
        t.CanFinish.Should().BeTrue();
    }

    [Fact]
    public void A_group_cycle_that_repeats_keeps_the_playoff_blocked()
    {
        var t = StartedGroupStage(TiebreakerPolicy.ComputedThenMatch);
        foreach (var m in t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList())
        {
            CompleteAWins(t, m);
        }

        t.StartTiebreakers();
        // Force each group's mini-league into a cycle as well, so nothing is separated by it.
        for (var g = 0; g < 2; g++)
        {
            CycleTiebreakers(t, g);
        }

        // The regression this pins: the playoff must NOT be startable while the tie is still live.
        t.CanStartPlayoffs.Should().BeFalse();
        t.NeedsTiebreakers.Should().BeTrue();
        t.Invoking(x => x.StartPlayoffs()).Should().Throw<DomainException>();
    }

    // ---- Uneven rosters ----

    private static Tournament StartedUnevenGroupStage(int participants, int groupCount)
    {
        var t = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.WinnerOnly, false, CreatedAt,
            groupCount: groupCount, tiebreakerPolicy: TiebreakerPolicy.ComputedThenMatch);
        for (var i = 1; i <= participants; i++)
        {
            t.AddParticipant($"P{i:00}");
        }

        t.DrawGroups();
        t.Start();
        return t;
    }

    /// <summary>Decides every pending group match with the stronger (lower within-group seed) participant winning, giving each group a strict order.</summary>
    private static void DecideGroupsStrictly(Tournament t)
    {
        foreach (var m in t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin && m.Status == MatchStatus.Pending).ToList())
        {
            var seedA = t.Participants.First(p => p.Id == m.ParticipantAId).Seed;
            var seedB = t.Participants.First(p => p.Id == m.ParticipantBId).Seed;
            t.CompleteMatch(m.Id, m.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, seedA < seedB) }, Now);
        }
    }

    [Fact]
    public void Nine_players_in_two_groups_send_eight_to_the_playoff_and_eliminate_the_last()
    {
        var t = StartedUnevenGroupStage(9, 2);
        DecideGroupsStrictly(t);

        t.NeedsTiebreakers.Should().BeFalse();   // strict group orders, and no level straddles a boundary
        t.CanStartPlayoffs.Should().BeTrue();
        t.StartPlayoffs();

        var seeded = t.Matches
            .Where(m => m.Segment is BracketSegment.Winner or BracketSegment.Loser or BracketSegment.GrandFinal)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();

        seeded.Should().HaveCount(8);
        // The one left out is the 5th-placed player of the five-strong group.
        var eliminated = t.Participants.Where(p => !seeded.Contains(p.Id)).ToList();
        eliminated.Should().ContainSingle();
        eliminated[0].Seed.Should().Be(5);
    }

    [Fact]
    public void Twelve_players_in_three_groups_play_a_cross_group_decider_for_the_contested_slots()
    {
        var t = StartedUnevenGroupStage(12, 3);
        DecideGroupsStrictly(t);

        // 8 of 12 advance across 3 groups, so the runners-up contest the Upper/Lower line and the
        // third-placed players contest the last two playoff slots.
        t.NeedsTiebreakers.Should().BeTrue();
        t.CanStartPlayoffs.Should().BeFalse();

        t.StartTiebreakers();
        var crossGroup = t.Matches.Where(m => m.Segment == BracketSegment.Tiebreaker && m.GroupIndex is null).ToList();
        crossGroup.Should().NotBeEmpty();
        crossGroup.Should().OnlyContain(m => m.MatchFormat == MatchFormat.Bo1);

        // Every decider is between players who finished at the same place in different groups.
        foreach (var match in crossGroup)
        {
            var a = t.Participants.First(p => p.Id == match.ParticipantAId);
            var b = t.Participants.First(p => p.Id == match.ParticipantBId);
            a.GroupIndex.Should().NotBe(b.GroupIndex);
        }

        foreach (var match in crossGroup)
        {
            var nameA = t.Participants.First(p => p.Id == match.ParticipantAId).Name;
            var nameB = t.Participants.First(p => p.Id == match.ParticipantBId).Name;
            t.CompleteMatch(
                match.Id, match.MatchFormat, ScoreType.WinnerOnly,
                new List<ScoreEntryInput> { new(null, null, string.CompareOrdinal(nameA, nameB) < 0) }, Now);
        }

        t.NeedsTiebreakers.Should().BeFalse();
        t.CanStartPlayoffs.Should().BeTrue();
        t.StartPlayoffs();

        var seeded = t.Matches
            .Where(m => m.Segment is BracketSegment.Winner or BracketSegment.Loser or BracketSegment.GrandFinal)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToHashSet();
        seeded.Should().HaveCount(8);
    }

    /// <summary>Completes a group's undecided 3-way tie-breaker mini-league as a cycle (x beats y, y beats z, z beats x), leaving it unresolved.</summary>
    private static void CycleTiebreakers(Tournament t, int? groupIndex)
    {
        var pending = t.Matches
            .Where(m => m.Segment == BracketSegment.Tiebreaker && m.GroupIndex == groupIndex && m.Status == MatchStatus.Pending)
            .ToList();
        var members = pending.SelectMany(m => new[] { m.ParticipantAId!.Value, m.ParticipantBId!.Value }).Distinct().ToList();
        members.Should().HaveCount(3);

        foreach (var match in pending)
        {
            // A beats B exactly when B is A's successor around the cycle.
            var aIndex = members.IndexOf(match.ParticipantAId!.Value);
            var bIndex = members.IndexOf(match.ParticipantBId!.Value);
            var aWins = (aIndex + 1) % members.Count == bIndex;
            t.CompleteMatch(match.Id, match.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, aWins) }, Now);
        }
    }

    [Fact]
    public void StartTiebreakers_is_rejected_while_the_current_wave_is_unplayed()
    {
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P1", "P4");
        Win(t, "P2", "P3");
        Win(t, "P3", "P4");
        Win(t, "P4", "P2");

        t.StartTiebreakers();

        t.NeedsTiebreakers.Should().BeFalse(); // a wave is in flight
        t.Invoking(x => x.StartTiebreakers()).Should().Throw<DomainException>().WithMessage("*Play out*");
    }

    [Fact]
    public void StartTiebreakers_is_rejected_when_no_tie_needs_one()
    {
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P1", "P4");
        Win(t, "P2", "P3");
        Win(t, "P2", "P4");
        Win(t, "P3", "P4");

        t.NeedsTiebreakers.Should().BeFalse();
        t.Invoking(x => x.StartTiebreakers()).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_tiebreaker_match_can_be_undone_like_a_flat_match()
    {
        var t = StartedRoundRobin(4, TiebreakerPolicy.ComputedThenMatch);
        Win(t, "P1", "P2");
        Win(t, "P1", "P3");
        Win(t, "P1", "P4");
        Win(t, "P2", "P3");
        Win(t, "P3", "P4");
        Win(t, "P4", "P2");
        t.StartTiebreakers();

        var tb = t.Matches.First(m => m.Segment == BracketSegment.Tiebreaker);
        var winnerIsA = true;
        t.CompleteMatch(tb.Id, tb.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, winnerIsA) }, Now);

        t.CanUndo(tb.Id).Should().BeTrue();
        t.UndoMatch(tb.Id);
        tb.WinnerId.Should().BeNull();
    }

    private static void Win(Tournament tournament, string winner, string loser, BracketSegment segment)
    {
        var match = RoundRobinMatch(tournament, winner, loser, segment);
        var winnerIsA = match.ParticipantAId == tournament.Participants.First(p => p.Name == winner).Id;
        tournament.CompleteMatch(match.Id, match.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, winnerIsA) }, Now);
    }

    // ---- Group Stage + Playoff ----

    private static Tournament StartedGroupStage(TiebreakerPolicy policy)
    {
        var t = Tournament.Create(
            "Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.WinnerOnly, false, CreatedAt, groupCount: 2, tiebreakerPolicy: policy);
        for (var i = 1; i <= 8; i++)
        {
            t.AddParticipant($"P{i}");
        }

        t.DrawGroups();
        t.Start();
        return t;
    }

    private static void CompleteAWins(Tournament t, Match m) =>
        t.CompleteMatch(m.Id, m.MatchFormat, ScoreType.WinnerOnly, new List<ScoreEntryInput> { new(null, null, true) }, Now);

    [Fact]
    public void A_group_cycle_blocks_the_playoff_until_tiebreakers_are_played()
    {
        // "A always wins" leaves each group's seeds 2-3-4 in a 1-2 cycle straddling the Upper/Lower line.
        var t = StartedGroupStage(TiebreakerPolicy.ComputedThenMatch);
        foreach (var m in t.Matches.Where(m => m.Segment == BracketSegment.RoundRobin).ToList())
        {
            CompleteAWins(t, m);
        }

        t.NeedsTiebreakers.Should().BeTrue();
        t.CanStartPlayoffs.Should().BeFalse();
        t.Invoking(x => x.StartPlayoffs()).Should().Throw<DomainException>();

        t.StartTiebreakers();
        t.Matches.Count(m => m.Segment == BracketSegment.Tiebreaker).Should().Be(6); // 3 per group, 2 groups
        t.CanStartPlayoffs.Should().BeFalse(); // tie-breakers pending

        foreach (var m in t.Matches.Where(m => m.Segment == BracketSegment.Tiebreaker).ToList())
        {
            CompleteAWins(t, m);
        }

        t.NeedsTiebreakers.Should().BeFalse();
        t.CanStartPlayoffs.Should().BeTrue();
        t.Invoking(x => x.StartPlayoffs()).Should().NotThrow();

        var upper = t.Matches.Where(m => m.Segment == BracketSegment.Winner && m.Round == 2)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId }).Where(id => id is not null).ToHashSet();
        var lower = t.Matches.Where(m => m.Segment == BracketSegment.Loser && m.Round == 1)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId }).Where(id => id is not null).ToHashSet();
        upper.Should().HaveCount(4);
        lower.Should().HaveCount(4);
        upper.Should().NotIntersectWith(lower);
    }
}
