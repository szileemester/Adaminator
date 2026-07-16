using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using FluentAssertions;

namespace Adaminator.Domain.Tests;

public class DoubleEliminationBracketTests
{
    private static readonly DateOnly Date = new(2026, 7, 14);
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 14, 10, 0, 0, TimeSpan.Zero);

    private static BracketMatchRef W(int round, int index) => new(BracketSegment.Winner, round, index);
    private static BracketMatchRef L(int round, int index) => new(BracketSegment.Loser, round, index);
    private static readonly BracketMatchRef GF = new(BracketSegment.GrandFinal, 1, 0);

    private static Tournament NewTournament() =>
        Tournament.Create("Cup", Date, null, TournamentType.DoubleElimination, MatchFormat.Bo3, thirdPlaceEnabled: false, CreatedAt);

    private static Tournament Seeded(int participantCount)
    {
        var tournament = NewTournament();
        for (var i = 1; i <= participantCount; i++)
        {
            tournament.AddParticipant($"P{i}");
        }

        var ordered = tournament.Participants.Select(p => p.Id).ToList();
        var byes = ordered.Take(DoubleEliminationBracket.ComputeRequiredByes(participantCount)).ToList();
        tournament.ApplySeeding(ordered, byes);
        return tournament;
    }

    // ---- Bracket size (AC-DE: capacities 4/8/16/32 only, floored at 4) ----

    [Theory]
    [InlineData(2, 4, 2)]
    [InlineData(3, 4, 1)]
    [InlineData(4, 4, 0)]
    [InlineData(5, 8, 3)]
    [InlineData(8, 8, 0)]
    [InlineData(13, 16, 3)]
    [InlineData(16, 16, 0)]
    [InlineData(32, 32, 0)]
    public void Bracket_size_and_byes_are_the_next_power_of_two_floored_at_four(int participants, int expectedSize, int expectedByes)
    {
        DoubleEliminationBracket.ComputeBracketSize(participants).Should().Be(expectedSize);
        DoubleEliminationBracket.ComputeRequiredByes(participants).Should().Be(expectedByes);
    }

    // ---- Structural (AC-DE-001): every supported capacity, repeated generation, round/match counts ----

    [Theory]
    [InlineData(4, 2, 2)]
    [InlineData(8, 3, 4)]
    [InlineData(16, 4, 6)]
    [InlineData(32, 5, 8)]
    public void Round_counts_match_the_spec_table(int capacity, int expectedWinnerRounds, int expectedLoserRounds)
    {
        DoubleEliminationBracket.WinnerRoundCount(capacity).Should().Be(expectedWinnerRounds);
        DoubleEliminationBracket.LoserRoundCount(capacity).Should().Be(expectedLoserRounds);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void GenerateTopology_is_deterministic_and_acyclic_with_unique_refs_and_exactly_one_grand_final(int capacity)
    {
        var first = DoubleEliminationBracket.GenerateTopology(capacity);
        var second = DoubleEliminationBracket.GenerateTopology(capacity);
        second.Should().BeEquivalentTo(first); // AC-DE-001: same input, same output.

        first.Select(m => m.Ref).Distinct().Should().HaveCount(first.Count); // unique match IDs.
        first.Count(m => m.Ref.Segment == BracketSegment.GrandFinal).Should().Be(1);

        // Acyclic: following WinnerTo (the only edge that can chain through Loser matches) from any
        // match must terminate at the Grand Final within a bounded number of hops.
        var byRef = first.ToDictionary(m => m.Ref);
        foreach (var match in first)
        {
            var seen = new HashSet<BracketMatchRef> { match.Ref };
            var current = match.WinnerTo;
            while (current is { } route)
            {
                seen.Should().NotContain(route.Target, "a cycle would mean the graph never terminates at the Grand Final");
                seen.Add(route.Target);
                current = byRef[route.Target].WinnerTo;
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void Every_destination_slot_has_at_most_one_incoming_route(int capacity)
    {
        var topology = DoubleEliminationBracket.GenerateTopology(capacity);
        var incoming = new List<(BracketMatchRef Target, bool SlotA)>();
        foreach (var m in topology)
        {
            if (m.WinnerTo is { } w)
            {
                incoming.Add((w.Target, w.SlotA));
            }

            if (m.LoserTo is { } l)
            {
                incoming.Add((l.Target, l.SlotA));
            }
        }

        incoming.Should().OnlyHaveUniqueItems();
    }

    // ---- Routing snapshot (AC-DE-002): the spec's own worked 8-slot example, locked verbatim. ----

    [Fact]
    public void GenerateTopology_8_slot_matches_the_approved_spec_example()
    {
        var byRef = DoubleEliminationBracket.GenerateTopology(8).ToDictionary(m => m.Ref);

        byRef[W(1, 0)].WinnerTo.Should().Be(new BracketRoute(W(2, 0), true));
        byRef[W(1, 0)].LoserTo.Should().Be(new BracketRoute(L(1, 0), true));
        byRef[W(1, 1)].WinnerTo.Should().Be(new BracketRoute(W(2, 0), false));
        byRef[W(1, 1)].LoserTo.Should().Be(new BracketRoute(L(1, 0), false));
        byRef[W(1, 2)].WinnerTo.Should().Be(new BracketRoute(W(2, 1), true));
        byRef[W(1, 2)].LoserTo.Should().Be(new BracketRoute(L(1, 1), true)); // WB3 loser -> LB2 A
        byRef[W(1, 3)].WinnerTo.Should().Be(new BracketRoute(W(2, 1), false));
        byRef[W(1, 3)].LoserTo.Should().Be(new BracketRoute(L(1, 1), false)); // WB4 loser -> LB2 B

        byRef[W(2, 0)].WinnerTo.Should().Be(new BracketRoute(W(3, 0), true));
        byRef[W(2, 0)].LoserTo.Should().Be(new BracketRoute(L(2, 1), false)); // WB5 loser -> LB4 B (crossover)
        byRef[W(2, 1)].WinnerTo.Should().Be(new BracketRoute(W(3, 0), false));
        byRef[W(2, 1)].LoserTo.Should().Be(new BracketRoute(L(2, 0), false)); // WB6 loser -> LB3 B (crossover)

        byRef[W(3, 0)].WinnerTo.Should().Be(new BracketRoute(GF, true));
        byRef[W(3, 0)].LoserTo.Should().Be(new BracketRoute(L(4, 0), false)); // WB Final loser -> LB Final B

        byRef[L(1, 0)].WinnerTo.Should().Be(new BracketRoute(L(2, 0), true)); // LB1 winner -> LB3 A
        byRef[L(1, 1)].WinnerTo.Should().Be(new BracketRoute(L(2, 1), true)); // LB2 winner -> LB4 A

        byRef[L(2, 0)].WinnerTo.Should().Be(new BracketRoute(L(3, 0), true)); // LB3 winner -> LB5 A
        byRef[L(2, 1)].WinnerTo.Should().Be(new BracketRoute(L(3, 0), false)); // LB4 winner -> LB5 B

        byRef[L(3, 0)].WinnerTo.Should().Be(new BracketRoute(L(4, 0), true)); // LB5 winner -> LB6 A

        byRef[L(4, 0)].WinnerTo.Should().Be(new BracketRoute(GF, false)); // LB Final winner -> GF B
    }

    // ---- Approved routing snapshots for the remaining capacities (spec section 16/19: reviewed
    // and explicitly approved by the user before being locked in as permanent fixtures; a snapshot
    // change requires the same explicit review). Generated by the same reversed-index crossover
    // rule verified above against the spec's own 8-slot example. ----

    [Fact]
    public void GenerateTopology_4_slot_matches_the_approved_snapshot()
    {
        var expected = new List<TopologyMatch>
        {
            new(W(1, 0), new BracketRoute(W(2, 0), true), new BracketRoute(L(1, 0), true)),
            new(W(1, 1), new BracketRoute(W(2, 0), false), new BracketRoute(L(1, 0), false)),
            new(W(2, 0), new BracketRoute(GF, true), new BracketRoute(L(2, 0), false)),
            new(L(1, 0), new BracketRoute(L(2, 0), true), null),
            new(L(2, 0), new BracketRoute(GF, false), null),
            new(GF, null, null),
        };

        DoubleEliminationBracket.GenerateTopology(4).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void GenerateTopology_16_slot_matches_the_approved_snapshot()
    {
        var expected = new List<TopologyMatch>
        {
            new(W(1, 0), new BracketRoute(W(2, 0), true), new BracketRoute(L(1, 0), true)),
            new(W(1, 1), new BracketRoute(W(2, 0), false), new BracketRoute(L(1, 0), false)),
            new(W(1, 2), new BracketRoute(W(2, 1), true), new BracketRoute(L(1, 1), true)),
            new(W(1, 3), new BracketRoute(W(2, 1), false), new BracketRoute(L(1, 1), false)),
            new(W(1, 4), new BracketRoute(W(2, 2), true), new BracketRoute(L(1, 2), true)),
            new(W(1, 5), new BracketRoute(W(2, 2), false), new BracketRoute(L(1, 2), false)),
            new(W(1, 6), new BracketRoute(W(2, 3), true), new BracketRoute(L(1, 3), true)),
            new(W(1, 7), new BracketRoute(W(2, 3), false), new BracketRoute(L(1, 3), false)),
            new(W(2, 0), new BracketRoute(W(3, 0), true), new BracketRoute(L(2, 3), false)),
            new(W(2, 1), new BracketRoute(W(3, 0), false), new BracketRoute(L(2, 2), false)),
            new(W(2, 2), new BracketRoute(W(3, 1), true), new BracketRoute(L(2, 1), false)),
            new(W(2, 3), new BracketRoute(W(3, 1), false), new BracketRoute(L(2, 0), false)),
            new(W(3, 0), new BracketRoute(W(4, 0), true), new BracketRoute(L(4, 1), false)),
            new(W(3, 1), new BracketRoute(W(4, 0), false), new BracketRoute(L(4, 0), false)),
            new(W(4, 0), new BracketRoute(GF, true), new BracketRoute(L(6, 0), false)),
            new(L(1, 0), new BracketRoute(L(2, 0), true), null),
            new(L(1, 1), new BracketRoute(L(2, 1), true), null),
            new(L(1, 2), new BracketRoute(L(2, 2), true), null),
            new(L(1, 3), new BracketRoute(L(2, 3), true), null),
            new(L(2, 0), new BracketRoute(L(3, 0), true), null),
            new(L(2, 1), new BracketRoute(L(3, 0), false), null),
            new(L(2, 2), new BracketRoute(L(3, 1), true), null),
            new(L(2, 3), new BracketRoute(L(3, 1), false), null),
            new(L(3, 0), new BracketRoute(L(4, 0), true), null),
            new(L(3, 1), new BracketRoute(L(4, 1), true), null),
            new(L(4, 0), new BracketRoute(L(5, 0), true), null),
            new(L(4, 1), new BracketRoute(L(5, 0), false), null),
            new(L(5, 0), new BracketRoute(L(6, 0), true), null),
            new(L(6, 0), new BracketRoute(GF, false), null),
            new(GF, null, null),
        };

        DoubleEliminationBracket.GenerateTopology(16).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void GenerateTopology_32_slot_matches_the_approved_snapshot()
    {
        var expected = new List<TopologyMatch>
        {
            new(W(1, 0), new BracketRoute(W(2, 0), true), new BracketRoute(L(1, 0), true)),
            new(W(1, 1), new BracketRoute(W(2, 0), false), new BracketRoute(L(1, 0), false)),
            new(W(1, 2), new BracketRoute(W(2, 1), true), new BracketRoute(L(1, 1), true)),
            new(W(1, 3), new BracketRoute(W(2, 1), false), new BracketRoute(L(1, 1), false)),
            new(W(1, 4), new BracketRoute(W(2, 2), true), new BracketRoute(L(1, 2), true)),
            new(W(1, 5), new BracketRoute(W(2, 2), false), new BracketRoute(L(1, 2), false)),
            new(W(1, 6), new BracketRoute(W(2, 3), true), new BracketRoute(L(1, 3), true)),
            new(W(1, 7), new BracketRoute(W(2, 3), false), new BracketRoute(L(1, 3), false)),
            new(W(1, 8), new BracketRoute(W(2, 4), true), new BracketRoute(L(1, 4), true)),
            new(W(1, 9), new BracketRoute(W(2, 4), false), new BracketRoute(L(1, 4), false)),
            new(W(1, 10), new BracketRoute(W(2, 5), true), new BracketRoute(L(1, 5), true)),
            new(W(1, 11), new BracketRoute(W(2, 5), false), new BracketRoute(L(1, 5), false)),
            new(W(1, 12), new BracketRoute(W(2, 6), true), new BracketRoute(L(1, 6), true)),
            new(W(1, 13), new BracketRoute(W(2, 6), false), new BracketRoute(L(1, 6), false)),
            new(W(1, 14), new BracketRoute(W(2, 7), true), new BracketRoute(L(1, 7), true)),
            new(W(1, 15), new BracketRoute(W(2, 7), false), new BracketRoute(L(1, 7), false)),
            new(W(2, 0), new BracketRoute(W(3, 0), true), new BracketRoute(L(2, 7), false)),
            new(W(2, 1), new BracketRoute(W(3, 0), false), new BracketRoute(L(2, 6), false)),
            new(W(2, 2), new BracketRoute(W(3, 1), true), new BracketRoute(L(2, 5), false)),
            new(W(2, 3), new BracketRoute(W(3, 1), false), new BracketRoute(L(2, 4), false)),
            new(W(2, 4), new BracketRoute(W(3, 2), true), new BracketRoute(L(2, 3), false)),
            new(W(2, 5), new BracketRoute(W(3, 2), false), new BracketRoute(L(2, 2), false)),
            new(W(2, 6), new BracketRoute(W(3, 3), true), new BracketRoute(L(2, 1), false)),
            new(W(2, 7), new BracketRoute(W(3, 3), false), new BracketRoute(L(2, 0), false)),
            new(W(3, 0), new BracketRoute(W(4, 0), true), new BracketRoute(L(4, 3), false)),
            new(W(3, 1), new BracketRoute(W(4, 0), false), new BracketRoute(L(4, 2), false)),
            new(W(3, 2), new BracketRoute(W(4, 1), true), new BracketRoute(L(4, 1), false)),
            new(W(3, 3), new BracketRoute(W(4, 1), false), new BracketRoute(L(4, 0), false)),
            new(W(4, 0), new BracketRoute(W(5, 0), true), new BracketRoute(L(6, 1), false)),
            new(W(4, 1), new BracketRoute(W(5, 0), false), new BracketRoute(L(6, 0), false)),
            new(W(5, 0), new BracketRoute(GF, true), new BracketRoute(L(8, 0), false)),
            new(L(1, 0), new BracketRoute(L(2, 0), true), null),
            new(L(1, 1), new BracketRoute(L(2, 1), true), null),
            new(L(1, 2), new BracketRoute(L(2, 2), true), null),
            new(L(1, 3), new BracketRoute(L(2, 3), true), null),
            new(L(1, 4), new BracketRoute(L(2, 4), true), null),
            new(L(1, 5), new BracketRoute(L(2, 5), true), null),
            new(L(1, 6), new BracketRoute(L(2, 6), true), null),
            new(L(1, 7), new BracketRoute(L(2, 7), true), null),
            new(L(2, 0), new BracketRoute(L(3, 0), true), null),
            new(L(2, 1), new BracketRoute(L(3, 0), false), null),
            new(L(2, 2), new BracketRoute(L(3, 1), true), null),
            new(L(2, 3), new BracketRoute(L(3, 1), false), null),
            new(L(2, 4), new BracketRoute(L(3, 2), true), null),
            new(L(2, 5), new BracketRoute(L(3, 2), false), null),
            new(L(2, 6), new BracketRoute(L(3, 3), true), null),
            new(L(2, 7), new BracketRoute(L(3, 3), false), null),
            new(L(3, 0), new BracketRoute(L(4, 0), true), null),
            new(L(3, 1), new BracketRoute(L(4, 1), true), null),
            new(L(3, 2), new BracketRoute(L(4, 2), true), null),
            new(L(3, 3), new BracketRoute(L(4, 3), true), null),
            new(L(4, 0), new BracketRoute(L(5, 0), true), null),
            new(L(4, 1), new BracketRoute(L(5, 0), false), null),
            new(L(4, 2), new BracketRoute(L(5, 1), true), null),
            new(L(4, 3), new BracketRoute(L(5, 1), false), null),
            new(L(5, 0), new BracketRoute(L(6, 0), true), null),
            new(L(5, 1), new BracketRoute(L(6, 1), true), null),
            new(L(6, 0), new BracketRoute(L(7, 0), true), null),
            new(L(6, 1), new BracketRoute(L(7, 0), false), null),
            new(L(7, 0), new BracketRoute(L(8, 0), true), null),
            new(L(8, 0), new BracketRoute(GF, false), null),
            new(GF, null, null),
        };

        DoubleEliminationBracket.GenerateTopology(32).Should().BeEquivalentTo(expected);
    }

    // ---- Bye cascade into the Loser Bracket (confirmed design decision) ----

    [Fact]
    public void Two_participants_collapse_the_entire_loser_bracket_into_a_grand_final_rematch()
    {
        var tournament = Seeded(2);

        tournament.Start();

        tournament.Matches.Should().HaveCount(2, "the Winner Bracket Final and the Grand Final are the only real matches");
        tournament.Matches.Should().ContainSingle(m => m.Segment == BracketSegment.Winner);
        tournament.Matches.Should().ContainSingle(m => m.Segment == BracketSegment.GrandFinal);
        tournament.Matches.Should().NotContain(m => m.Segment == BracketSegment.Loser);

        var wbFinal = tournament.Matches.Single(m => m.Segment == BracketSegment.Winner);
        var grandFinal = tournament.Matches.Single(m => m.Segment == BracketSegment.GrandFinal);
        wbFinal.LoserToMatchId.Should().Be(grandFinal.Id, "the Winner Bracket Final's loser cascades straight to the Grand Final - there is no Loser Bracket Final to play");
        wbFinal.LoserToSlotA.Should().BeFalse();
    }

    [Fact]
    public void Three_participants_produce_a_real_loser_bracket_final_and_third_place()
    {
        var tournament = Seeded(3);

        tournament.Start();

        tournament.Matches.Should().ContainSingle(m => m.Segment == BracketSegment.Loser,
            "one Loser Bracket match (the Loser Bracket Final) should survive the cascade - the round-1 Loser Bracket match collapses since only one real Winner Bracket round-1 match exists");
        var loserFinal = tournament.Matches.Single(m => m.Segment == BracketSegment.Loser);
        var grandFinal = tournament.Matches.Single(m => m.Segment == BracketSegment.GrandFinal);
        loserFinal.WinnerToMatchId.Should().Be(grandFinal.Id);
        loserFinal.WinnerToSlotA.Should().BeFalse();
    }

    [Fact]
    public void Five_participants_in_an_eight_slot_bracket_collapse_the_first_two_loser_rounds()
    {
        var tournament = Seeded(5);

        tournament.Start();

        var loserMatches = tournament.Matches.Where(m => m.Segment == BracketSegment.Loser).ToList();
        loserMatches.Should().HaveCount(3, "only Loser rounds 2, 3 and 4 end up with two real entrants; rounds where a real Winner Bracket loser would meet a bye never materialize");
        loserMatches.Select(m => m.Round).Should().BeEquivalentTo(new[] { 2, 3, 4 });
    }
}
