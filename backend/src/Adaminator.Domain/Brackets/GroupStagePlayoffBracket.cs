using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Brackets;

/// <summary>
/// TI-style two-stage bracket construction. Stage 1 is either a per-group round robin (reusing
/// <see cref="RoundRobinBracket.Schedule"/>) or one Swiss pool (see <see cref="SwissBracket"/>);
/// stage 2 is a playoff built later from the resulting standings. Which of the four combinations a
/// tournament plays is set by its <see cref="GroupStageKind"/> and <see cref="PlayoffKind"/>.
///
/// A <see cref="PlayoffKind.DoubleElimination"/> playoff reuses
/// <see cref="DoubleEliminationBracket.GenerateTopology"/> wholesale - the K upper seeds occupy the
/// Winner Bracket's round 2 (a round-1 bye) and the K lower seeds fill the Loser Bracket's round 1
/// (where Winner Bracket round-1 losers would normally arrive), so Winner round 1 is never
/// materialized. A <see cref="PlayoffKind.SingleElimination"/> playoff seeds every qualifier into one
/// bye-free tree instead. Either way the playoff capacity is a clean power of two, so no bye cascade
/// is ever needed.
/// </summary>
public static class GroupStagePlayoffBracket
{
    /// <summary>The playoff sizes an admin may cut to - the same set for both playoff kinds.</summary>
    public static IReadOnlyList<int> SupportedPlayoffSizes => DoubleEliminationBracket.SupportedCapacities;

    /// <summary>
    /// The default cut when the admin hasn't chosen one: the largest supported capacity that fits the
    /// roster (4, 8, 16 or 32). Anyone beyond it is eliminated at the end of the group stage, so the
    /// playoff itself is always a clean power of two.
    /// </summary>
    public static int LargestCapacityFor(int participantCount) =>
        SupportedPlayoffSizes
            .Where(c => c <= participantCount)
            .DefaultIfEmpty(DoubleEliminationBracket.MinCapacity)
            .Max();

    /// <summary>
    /// The seed-order indices where crossing changes a participant's fate: the Upper/Lower split (a
    /// double-elimination playoff only) and the playoff cut itself. The one definition of "a boundary
    /// worth playing off", shared by <see cref="PlanLevels"/> and the tie-breaker search.
    /// </summary>
    public static IReadOnlyList<int> SeedCuts(int capacity, PlayoffKind playoffKind) =>
        playoffKind == PlayoffKind.DoubleElimination
            ? new[] { capacity / 2, capacity }
            : new[] { capacity };

    /// <summary>Group sizes for <paramref name="participantCount"/> dealt into <paramref name="groupCount"/> groups, as even as possible with the remainder going to the earlier groups.</summary>
    public static IReadOnlyList<int> GroupSizes(int participantCount, int groupCount)
    {
        var baseSize = participantCount / groupCount;
        var remainder = participantCount % groupCount;
        return Enumerable.Range(0, groupCount).Select(g => baseSize + (g < remainder ? 1 : 0)).ToList();
    }

    /// <summary>
    /// Validates a chosen playoff size on its own, without a roster - the check available when the
    /// tournament's settings are set, before anyone has been added.
    /// </summary>
    public static void ValidateSupportedSize(int size)
    {
        if (!SupportedPlayoffSizes.Contains(size))
        {
            throw new DomainException(
                $"A playoff of {size} is not supported; choose one of {string.Join(", ", SupportedPlayoffSizes)}.");
        }
    }

    /// <summary>
    /// Validates the playoff cut against the roster it has to be filled from, at start time. Applies to
    /// both group-stage kinds - the cut is a property of the playoff, not of how the group stage was played.
    /// </summary>
    public static void ValidatePlayoffCapacity(int participantCount, int capacity)
    {
        ValidateSupportedSize(capacity);

        if (capacity > participantCount)
        {
            throw new DomainException(
                $"A playoff of {capacity} needs at least {capacity} participants; {participantCount} given.");
        }
    }

    /// <summary>Validates the group/participant shape of a round-robin group stage; thrown at tournament start (mirrors the other builders' start-time validation).</summary>
    public static void ValidateShape(int participantCount, int groupCount)
    {
        if (participantCount < DoubleEliminationBracket.MinCapacity)
        {
            throw new DomainException(
                $"Group Stage + Playoff needs at least {DoubleEliminationBracket.MinCapacity} participants; {participantCount} given.");
        }

        if (groupCount < 2)
        {
            throw new DomainException("Group Stage + Playoff needs at least 2 groups.");
        }

        // Every group has to actually play a round robin, so it needs at least two participants.
        if (groupCount * 2 > participantCount)
        {
            throw new DomainException(
                $"{participantCount} participants cannot fill {groupCount} groups of at least 2; use at most {participantCount / 2} groups.");
        }
    }

    /// <summary>
    /// Builds the group-stage matches: one round robin per group, each match tagged with its group index.
    /// A Best-of-2 group plays draw-capable <see cref="MatchFormat.Bo2"/> matches; the playoff reads its
    /// own, always-decisive segment formats.
    /// </summary>
    public static List<Match> BuildGroupStage(Tournament tournament)
    {
        var format = tournament.GroupStageMatchFormat;
        var scoreType = tournament.DefaultScoreType;

        var matches = new List<Match>();
        for (var g = 0; g < tournament.GroupCount; g++)
        {
            var ids = tournament.Participants
                .Where(p => p.GroupIndex == g)
                .OrderBy(p => p.Seed)
                .Select(p => p.Id)
                .ToList();

            matches.AddRange(RoundRobinBracket.Schedule(tournament.Id, ids, format, scoreType, groupIndex: g));
        }

        return matches;
    }

    /// <summary>
    /// One "placement level": everyone who finished <see cref="Position"/>-th in their own group.
    /// Participants are seeded level by level (all group winners, then all runners-up, …), so a level's
    /// members occupy global seed indices <see cref="Start"/>..<see cref="End"/>.
    /// </summary>
    public readonly record struct PlacementLevel(int Position, int Start, int End, LevelOutcome Outcome)
    {
        /// <summary>How many participants finished at this placement - one per group large enough to have the position.</summary>
        public int Size => End - Start + 1;
    }

    /// <summary>
    /// Levels for the given group sizes. Sizes and positions depend only on how many participants each
    /// group holds - never on results - so the whole plan is known the moment the groups are drawn.
    /// </summary>
    public static IReadOnlyList<PlacementLevel> PlanLevels(
        IReadOnlyList<int> groupSizes, int capacity, PlayoffKind playoffKind)
    {
        var cuts = SeedCuts(capacity, playoffKind);
        var levels = new List<PlacementLevel>();
        var start = 0;

        for (var position = 1; position <= groupSizes.Max(); position++)
        {
            var size = groupSizes.Count(s => s >= position);
            var end = start + size - 1;
            levels.Add(new PlacementLevel(position, start, end, Classify(start, end, cuts, capacity)));
            start += size;
        }

        return levels;
    }

    /// <summary>
    /// Whether a cut falls strictly inside the span <paramref name="start"/>..<paramref name="end"/> -
    /// i.e. the span's members sit on both sides of it and are competing for the slots either side.
    /// The one definition of "straddles a boundary", shared with <see cref="RoundRobinStandings"/>.
    /// </summary>
    public static bool SpansCut(int start, int end, int cut) => start < cut && cut <= end;

    /// <summary>
    /// Where a span of seed indices lands, given the cuts that matter for this playoff kind. A single
    /// cut (single elimination) means there is no Lower pool at all - everyone inside the capacity is
    /// <see cref="LevelOutcome.Upper"/>, the playoff's one pool.
    /// </summary>
    private static LevelOutcome Classify(int start, int end, IReadOnlyList<int> cuts, int capacity)
    {
        if (cuts.Any(cut => SpansCut(start, end, cut)))
        {
            return LevelOutcome.Contested;
        }

        if (end >= capacity)
        {
            return LevelOutcome.Eliminated;
        }

        return cuts.Count > 1 && end >= cuts[0] ? LevelOutcome.Lower : LevelOutcome.Upper;
    }

    /// <summary>
    /// Where a single seed index lands. Used for a Swiss pool, whose standings are one flat ordered list
    /// rather than interleaved group placements - a single index can never straddle a cut, so this is
    /// never <see cref="LevelOutcome.Contested"/>.
    /// </summary>
    public static LevelOutcome ClassifySeedIndex(int index, int capacity, PlayoffKind playoffKind) =>
        Classify(index, index, SeedCuts(capacity, playoffKind), capacity);

    /// <summary>The participants at one placement level - each group's <paramref name="position"/>-th finisher, for every group that has one.</summary>
    public static List<Guid> LevelMembers(IReadOnlyList<IReadOnlyList<Guid>> groupStandings, int position) =>
        groupStandings.Where(g => g.Count >= position).Select(g => g[position - 1]).ToList();

    /// <summary>
    /// Splits a fully ordered seeding list into the playoff's entry pools and the participants who fall
    /// outside the capacity and are eliminated at the group stage. A double-elimination playoff fills
    /// both pools (Winner Bracket, then Loser Bracket); a single-elimination playoff has one bracket, so
    /// every qualifier lands in <c>Upper</c> and <c>Lower</c> is empty.
    /// </summary>
    public static (List<Guid> Upper, List<Guid> Lower, List<Guid> Eliminated) SeedPools(
        IReadOnlyList<Guid> seedOrder, int capacity, PlayoffKind playoffKind)
    {
        var upperSize = playoffKind == PlayoffKind.DoubleElimination ? capacity / 2 : capacity;
        return (seedOrder.Take(upperSize).ToList(),
                seedOrder.Skip(upperSize).Take(capacity - upperSize).ToList(),
                seedOrder.Skip(capacity).ToList());
    }

    /// <summary>
    /// The positions inside a group of <paramref name="groupSize"/> where finishing one place lower
    /// changes a participant's fate (Upper vs Lower vs eliminated, or drops them into a contested
    /// level). These are the cuts a within-group tie has to straddle to be worth playing off.
    /// </summary>
    public static IReadOnlyList<int> GroupBoundaryCuts(IReadOnlyList<PlacementLevel> levels, int groupSize)
    {
        var cuts = new List<int>();
        for (var position = 1; position < groupSize; position++)
        {
            var here = levels[position - 1];
            var next = levels[position];
            if (here.Outcome != next.Outcome || here.Outcome == LevelOutcome.Contested)
            {
                cuts.Add(position);
            }
        }

        return cuts;
    }

    /// <summary>
    /// Builds the playoff match graph from the ordered seed pools, in whichever shape the tournament's
    /// <see cref="Entities.Tournament.PlayoffKind"/> calls for.
    /// </summary>
    public static List<Match> BuildPlayoff(Tournament tournament, IReadOnlyList<Guid> upperSeeds, IReadOnlyList<Guid> lowerSeeds) =>
        tournament.PlayoffKind == PlayoffKind.SingleElimination
            ? BuildSingleEliminationPlayoff(tournament, upperSeeds)
            : BuildDoubleEliminationPlayoff(tournament, upperSeeds, lowerSeeds);

    /// <summary>
    /// One bye-free single-elimination tree over every qualifier, seeded adjacently in order (seeds 0v1,
    /// 2v3, …) - the same convention <see cref="SingleEliminationBracket.ComputeRound1Pairings"/> uses
    /// once byes are exhausted, and the same one the double-elimination playoff seeds its pools with.
    ///
    /// Unlike a standalone Single Elimination tournament, which recomputes its one forward route from
    /// round math, this stores explicit routes like the double-elimination playoff does. That keeps
    /// every Group Stage + Playoff on one advancement mechanism regardless of playoff kind, so
    /// advancement and undo need no per-kind branching.
    /// </summary>
    private static List<Match> BuildSingleEliminationPlayoff(Tournament tournament, IReadOnlyList<Guid> seeds)
    {
        var capacity = seeds.Count;
        var rounds = SingleEliminationBracket.RoundCount(capacity);
        var format = tournament.PlayoffFormatFor(BracketSegment.Winner);
        var scoreType = tournament.DefaultScoreType;

        var byPosition = new Dictionary<(int Round, int Index), Match>();
        for (var round = 1; round <= rounds; round++)
        {
            for (var index = 0; index < capacity >> round; index++)
            {
                var seeded = round == 1;
                byPosition[(round, index)] = Match.Create(
                    tournament.Id,
                    BracketSegment.Winner,
                    round,
                    index,
                    seeded ? seeds[2 * index] : null,
                    seeded ? seeds[2 * index + 1] : null,
                    format,
                    scoreType);
            }
        }

        // A Third Place match is fed by the two semifinal losers, mirroring how their winners feed the
        // Final's slots A/B.
        Match? thirdPlace = null;
        if (tournament.ThirdPlaceEnabled && rounds >= 2)
        {
            thirdPlace = Match.Create(tournament.Id, BracketSegment.ThirdPlace, rounds, 0, null, null, format, scoreType);
        }

        foreach (var ((round, index), match) in byPosition)
        {
            var next = SingleEliminationBracket.NextWinnerSlot(round, index, rounds);
            var winnerTo = next is { } slot ? byPosition[(slot.Round, slot.IndexInRound)] : null;
            var loserTo = thirdPlace is not null && round == rounds - 1 ? thirdPlace : null;

            match.SetRoutes(
                winnerTo?.Id,
                next?.SlotA,
                loserTo?.Id,
                loserTo is null ? null : SingleEliminationBracket.ThirdPlaceSlotAFromSemifinalIndex(index));
        }

        var matches = byPosition.Values.ToList();
        if (thirdPlace is not null)
        {
            matches.Add(thirdPlace);
        }

        return matches;
    }

    /// <summary>
    /// <paramref name="upperSeeds"/> fill the Winner Bracket round 2 (pair-wise, in order) and
    /// <paramref name="lowerSeeds"/> the Loser Bracket round 1; every other match starts empty and is
    /// filled by advancement along the routes resolved here (same mechanism as
    /// <see cref="DoubleEliminationBracket"/>).
    /// </summary>
    private static List<Match> BuildDoubleEliminationPlayoff(Tournament tournament, IReadOnlyList<Guid> upperSeeds, IReadOnlyList<Guid> lowerSeeds)
    {
        var capacity = upperSeeds.Count + lowerSeeds.Count;
        var topology = DoubleEliminationBracket.GenerateTopology(capacity);
        var scoreType = tournament.DefaultScoreType;
        var winnerFormat = tournament.PlayoffFormatFor(BracketSegment.Winner);
        var loserFormat = tournament.PlayoffFormatFor(BracketSegment.Loser);
        var grandFinalFormat = tournament.PlayoffFormatFor(BracketSegment.GrandFinal);

        var byRef = new Dictionary<BracketMatchRef, Match>();
        foreach (var topologyMatch in topology)
        {
            var reference = topologyMatch.Ref;

            // Winner round 1 is replaced by direct seeding into round 2 (upper) and round 1 of the
            // Loser Bracket (lower), so it is never materialized.
            if (reference.Segment == BracketSegment.Winner && reference.Round == 1)
            {
                continue;
            }

            Guid? a = null;
            Guid? b = null;
            if (reference.Segment == BracketSegment.Winner && reference.Round == 2)
            {
                a = upperSeeds[2 * reference.IndexInRound];
                b = upperSeeds[2 * reference.IndexInRound + 1];
            }
            else if (reference.Segment == BracketSegment.Loser && reference.Round == 1)
            {
                a = lowerSeeds[2 * reference.IndexInRound];
                b = lowerSeeds[2 * reference.IndexInRound + 1];
            }

            var format = reference.Segment switch
            {
                BracketSegment.Winner => winnerFormat,
                BracketSegment.Loser => loserFormat,
                _ => grandFinalFormat,
            };
            byRef[reference] = Match.Create(
                tournament.Id, reference.Segment, reference.Round, reference.IndexInRound, a, b, format, scoreType);
        }

        // Every target is a real match here (no bye cascade to hop over), so no route resolver is needed.
        DoubleEliminationBracket.ApplyRoutes(byRef, topology.ToDictionary(t => t.Ref));

        return byRef.Values.ToList();
    }
}
