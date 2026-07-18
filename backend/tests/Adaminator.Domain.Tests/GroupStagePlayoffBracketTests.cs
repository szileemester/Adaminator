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
    [InlineData(6, 2)]   // not a power of two
    [InlineData(8, 3)]   // 3 does not divide 8
    [InlineData(8, 1)]   // fewer than 2 groups
    [InlineData(8, 8)]   // group size 1 - cannot split into halves
    public void ValidateShape_rejects_unsupported_shapes(int participants, int groups)
    {
        FluentActions.Invoking(() => GroupStagePlayoffBracket.ValidateShape(participants, groups)).Should().Throw<DomainException>();
    }

    // ---- Seed pools ----

    [Fact]
    public void SeedPools_splits_top_and_bottom_half_interleaving_across_groups()
    {
        var g0 = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var g1 = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var (upper, lower) = GroupStagePlayoffBracket.SeedPools(new[] { (IReadOnlyList<Guid>)g0, g1 });

        // Ranks 1-2 of each group, interleaved by rank across groups.
        upper.Should().Equal(g0[0], g1[0], g0[1], g1[1]);
        // Ranks 3-4 of each group.
        lower.Should().Equal(g0[2], g1[2], g0[3], g1[3]);
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
