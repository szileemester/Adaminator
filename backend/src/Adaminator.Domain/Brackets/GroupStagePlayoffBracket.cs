using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Brackets;

/// <summary>
/// TI-style two-stage bracket construction. Stage 1 is a per-group round robin (reusing
/// <see cref="RoundRobinBracket.Schedule"/>). Stage 2 is a standard double-elimination playoff, built
/// later from the group standings: each group's top half enters the Winner Bracket and its bottom
/// half the Loser Bracket. The playoff reuses <see cref="DoubleEliminationBracket.GenerateTopology"/>
/// wholesale - the K upper seeds occupy the Winner Bracket's round 2 (a round-1 bye) and the K lower
/// seeds fill the Loser Bracket's round 1 (where Winner Bracket round-1 losers would normally arrive),
/// so Winner round 1 is simply never materialized. v1 requires a power-of-two participant count so the
/// playoff is bye-free.
/// </summary>
public static class GroupStagePlayoffBracket
{
    /// <summary>
    /// How many participants reach the playoff: the largest bye-free double-elimination capacity that
    /// fits the roster (4, 8, 16 or 32). Anyone the roster carries beyond that is eliminated at the end
    /// of the group stage, so the playoff itself is always a clean power of two.
    /// </summary>
    public static int PlayoffCapacity(int participantCount) =>
        DoubleEliminationBracket.SupportedCapacities
            .Where(c => c <= participantCount)
            .DefaultIfEmpty(DoubleEliminationBracket.MinCapacity)
            .Max();

    /// <summary>Group sizes for <paramref name="participantCount"/> dealt into <paramref name="groupCount"/> groups, as even as possible with the remainder going to the earlier groups.</summary>
    public static IReadOnlyList<int> GroupSizes(int participantCount, int groupCount)
    {
        var baseSize = participantCount / groupCount;
        var remainder = participantCount % groupCount;
        return Enumerable.Range(0, groupCount).Select(g => baseSize + (g < remainder ? 1 : 0)).ToList();
    }

    /// <summary>Validates the group/participant shape; thrown at tournament start (mirrors the other builders' start-time validation).</summary>
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

    /// <summary>Builds the group-stage matches: one round robin per group, each match tagged with its group index.</summary>
    public static List<Match> BuildGroupStage(Tournament tournament)
    {
        var matches = new List<Match>();
        for (var g = 0; g < tournament.GroupCount; g++)
        {
            var ids = tournament.Participants
                .Where(p => p.GroupIndex == g)
                .OrderBy(p => p.Seed)
                .Select(p => p.Id)
                .ToList();

            matches.AddRange(RoundRobinBracket.Schedule(
                tournament.Id, ids, tournament.DefaultMatchFormat, tournament.DefaultScoreType, groupIndex: g));
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
    public static IReadOnlyList<PlacementLevel> PlanLevels(IReadOnlyList<int> groupSizes, int capacity)
    {
        var upperCut = capacity / 2;
        var levels = new List<PlacementLevel>();
        var start = 0;

        for (var position = 1; position <= groupSizes.Max(); position++)
        {
            var size = groupSizes.Count(s => s >= position);
            var end = start + size - 1;
            levels.Add(new PlacementLevel(position, start, end, Classify(start, end, upperCut, capacity)));
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

    private static LevelOutcome Classify(int start, int end, int upperCut, int capacity)
    {
        if (SpansCut(start, end, upperCut) || SpansCut(start, end, capacity))
        {
            return LevelOutcome.Contested;
        }

        if (end < upperCut)
        {
            return LevelOutcome.Upper;
        }

        return end < capacity ? LevelOutcome.Lower : LevelOutcome.Eliminated;
    }

    /// <summary>The participants at one placement level - each group's <paramref name="position"/>-th finisher, for every group that has one.</summary>
    public static List<Guid> LevelMembers(IReadOnlyList<IReadOnlyList<Guid>> groupStandings, int position) =>
        groupStandings.Where(g => g.Count >= position).Select(g => g[position - 1]).ToList();

    /// <summary>
    /// Splits a fully ordered seeding list into the Winner Bracket pool, the Loser Bracket pool, and the
    /// participants who fall outside the playoff capacity and are eliminated at the group stage.
    /// </summary>
    public static (List<Guid> Upper, List<Guid> Lower, List<Guid> Eliminated) SeedPools(IReadOnlyList<Guid> seedOrder, int capacity) =>
        (seedOrder.Take(capacity / 2).ToList(),
         seedOrder.Skip(capacity / 2).Take(capacity - capacity / 2).ToList(),
         seedOrder.Skip(capacity).ToList());

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
    /// Builds the playoff match graph from the ordered seed pools. <paramref name="upperSeeds"/> fill
    /// the Winner Bracket round 2 (pair-wise, in order) and <paramref name="lowerSeeds"/> the Loser
    /// Bracket round 1; every other match starts empty and is filled by advancement along the routes
    /// resolved here (same mechanism as <see cref="DoubleEliminationBracket"/>).
    /// </summary>
    public static List<Match> BuildPlayoff(Tournament tournament, IReadOnlyList<Guid> upperSeeds, IReadOnlyList<Guid> lowerSeeds)
    {
        var capacity = upperSeeds.Count + lowerSeeds.Count;
        var topology = DoubleEliminationBracket.GenerateTopology(capacity);
        var format = tournament.DefaultMatchFormat;
        var scoreType = tournament.DefaultScoreType;

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

            byRef[reference] = Match.Create(tournament.Id, reference.Segment, reference.Round, reference.IndexInRound, a, b, format, scoreType);
        }

        // Every target is a real match here (no bye cascade to hop over), so no route resolver is needed.
        DoubleEliminationBracket.ApplyRoutes(byRef, topology.ToDictionary(t => t.Ref));

        return byRef.Values.ToList();
    }
}
