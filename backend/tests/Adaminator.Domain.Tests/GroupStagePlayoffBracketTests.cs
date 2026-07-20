using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class GroupStagePlayoffBracketTests
{
    private static readonly DateOnly Date = new(2026, 7, 18);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);

    private static Tournament NewTournament(int groupCount) =>
        Tournament.Create("Major", Date, null, TournamentType.GroupStagePlayoff, MatchFormat.Bo1, ScoreType.WinnerOnly, false, CreatedAt, groupCount);

    // ---- Shape validation ----

    [Theory]
    [InlineData(4, 2)]
    [InlineData(8, 2)]
    [InlineData(16, 2)]
    [InlineData(16, 4)]
    [InlineData(32, 2)]
    public void ValidateShape_accepts_supported_shapes(int participants, int groups)
    {
        FluentActions.Invoking(() => GroupStagePlayoffBracket.ValidateShape(participants, groups)).Should().NotThrow();
    }

    [Theory]
    [InlineData(3, 2)]   // too few for even the smallest playoff
    [InlineData(8, 1)]   // fewer than 2 groups
    [InlineData(8, 8)]   // groups of 1 have no matches to play
    public void ValidateShape_rejects_unsupported_shapes(int participants, int groups)
    {
        FluentActions.Invoking(() => GroupStagePlayoffBracket.ValidateShape(participants, groups)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(9, 2)]   // uneven groups (5 + 4)
    [InlineData(12, 3)]  // playoff slots do not divide across the groups
    [InlineData(6, 2)]   // not a power of two
    [InlineData(32, 4)]
    public void ValidateShape_accepts_any_roster_that_fills_its_groups(int participants, int groups)
    {
        FluentActions.Invoking(() => GroupStagePlayoffBracket.ValidateShape(participants, groups)).Should().NotThrow();
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]    // the 9th player misses out
    [InlineData(15, 8)]
    [InlineData(16, 16)]
    [InlineData(31, 16)]
    public void PlayoffCapacity_is_the_largest_power_of_two_the_roster_fills(int participants, int expected)
    {
        GroupStagePlayoffBracket.PlayoffCapacity(participants).Should().Be(expected);
    }

    [Theory]
    [InlineData(9, 2, new[] { 5, 4 })]
    [InlineData(12, 3, new[] { 4, 4, 4 })]
    [InlineData(10, 4, new[] { 3, 3, 2, 2 })]
    public void GroupSizes_spread_the_remainder_over_the_earlier_groups(int participants, int groups, int[] expected)
    {
        GroupStagePlayoffBracket.GroupSizes(participants, groups).Should().Equal(expected);
    }

    // ---- Placement levels ----

    [Fact]
    public void Levels_for_an_exact_power_of_two_are_a_clean_split_with_no_contest()
    {
        var levels = GroupStagePlayoffBracket.PlanLevels(new[] { 4, 4 }, capacity: 8);

        levels.Select(l => l.Outcome).Should()
            .Equal(LevelOutcome.Upper, LevelOutcome.Upper, LevelOutcome.Lower, LevelOutcome.Lower);
    }

    [Fact]
    public void Levels_for_nine_players_in_two_groups_eliminate_only_the_odd_one_out()
    {
        // Groups of 5 and 4: eight advance (top four of each), the 5th-placed player is out.
        var levels = GroupStagePlayoffBracket.PlanLevels(new[] { 5, 4 }, capacity: 8);

        levels.Select(l => l.Outcome).Should()
            .Equal(LevelOutcome.Upper, LevelOutcome.Upper, LevelOutcome.Lower, LevelOutcome.Lower, LevelOutcome.Eliminated);
        levels[4].Size.Should().Be(1); // only the bigger group reaches a 5th place
    }

    [Fact]
    public void Levels_contest_the_slots_when_the_playoff_does_not_divide_across_groups()
    {
        // 12 players in 3 groups: 8 advance, so the runners-up contest the Upper/Lower line and the
        // third-placed players contest the last playoff slots.
        var levels = GroupStagePlayoffBracket.PlanLevels(new[] { 4, 4, 4 }, capacity: 8);

        levels.Select(l => l.Outcome).Should()
            .Equal(LevelOutcome.Upper, LevelOutcome.Contested, LevelOutcome.Contested, LevelOutcome.Eliminated);
    }

    // ---- Seed pools ----

    [Fact]
    public void SeedPools_splits_the_seed_order_into_upper_lower_and_eliminated()
    {
        var ids = NewIds(9);

        var (upper, lower, eliminated) = GroupStagePlayoffBracket.SeedPools(ids, capacity: 8);

        upper.Should().Equal(ids.Take(4));
        lower.Should().Equal(ids.Skip(4).Take(4));
        eliminated.Should().Equal(ids[8]);
    }

    // ---- Playoff construction ----

    [Theory]
    [InlineData(4, 4)]
    [InlineData(8, 10)]
    [InlineData(16, 22)]
    public void Playoff_reuses_the_double_elimination_topology_minus_winner_round_one(int capacity, int expectedMatchCount)
    {
        var tournament = NewTournament(2);
        var upper = NewIds(capacity / 2);
        var lower = NewIds(capacity / 2);

        var matches = GroupStagePlayoffBracket.BuildPlayoff(tournament, upper, lower);

        matches.Should().HaveCount(expectedMatchCount);
        matches.Should().NotContain(m => m.Segment == BracketSegment.Winner && m.Round == 1);
        matches.Count(m => m.Segment == BracketSegment.GrandFinal).Should().Be(1);

        // Every non-Grand-Final match feeds its winner somewhere (routing continuity).
        matches.Where(m => m.Segment != BracketSegment.GrandFinal)
            .Should().OnlyContain(m => m.WinnerToMatchId != null);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void Playoff_seeds_upper_into_winner_round_two_and_lower_into_loser_round_one(int capacity)
    {
        var tournament = NewTournament(2);
        var upper = NewIds(capacity / 2);
        var lower = NewIds(capacity / 2);

        var matches = GroupStagePlayoffBracket.BuildPlayoff(tournament, upper, lower);

        var winnerRoundTwoSlots = matches
            .Where(m => m.Segment == BracketSegment.Winner && m.Round == 2)
            .OrderBy(m => m.IndexInRound)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId });
        winnerRoundTwoSlots.Should().Equal(upper.Select(id => (Guid?)id));

        var loserRoundOneSlots = matches
            .Where(m => m.Segment == BracketSegment.Loser && m.Round == 1)
            .OrderBy(m => m.IndexInRound)
            .SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId });
        loserRoundOneSlots.Should().Equal(lower.Select(id => (Guid?)id));

        // No other playoff match starts with a known participant - everyone else advances in.
        matches.Where(m => !(m.Segment == BracketSegment.Winner && m.Round == 2) && !(m.Segment == BracketSegment.Loser && m.Round == 1))
            .Should().OnlyContain(m => m.ParticipantAId == null && m.ParticipantBId == null);
    }

    private static List<Guid> NewIds(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
}
