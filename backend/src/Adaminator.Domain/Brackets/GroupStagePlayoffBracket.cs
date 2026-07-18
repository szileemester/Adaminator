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
    /// Participant counts whose playoff is a clean, bye-free double elimination (upper = lower = N/2,
    /// itself a power of two). Every participant reaches the playoff, so this is exactly the set of
    /// capacities the underlying Double Elimination topology supports.
    /// </summary>
    public static IReadOnlyList<int> SupportedParticipantCounts => DoubleEliminationBracket.SupportedCapacities;

    /// <summary>Validates the group/participant shape; thrown at tournament start (mirrors the other builders' start-time validation).</summary>
    public static void ValidateShape(int participantCount, int groupCount)
    {
        if (!SupportedParticipantCounts.Contains(participantCount))
        {
            throw new DomainException(
                $"Group Stage + Playoff supports {string.Join(", ", SupportedParticipantCounts)} participants (a power of two); {participantCount} given.");
        }

        if (groupCount < 2)
        {
            throw new DomainException("Group Stage + Playoff needs at least 2 groups.");
        }

        if (participantCount % groupCount != 0)
        {
            throw new DomainException($"{participantCount} participants do not divide evenly into {groupCount} groups.");
        }

        if (participantCount / groupCount % 2 != 0)
        {
            throw new DomainException(
                $"Each group must have an even number of participants so it splits into top/bottom halves; {participantCount / groupCount} per group.");
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
    /// Splits per-group standings (each best-&gt;worst) into the upper and lower playoff seed pools.
    /// Interleaves across groups by rank (every group's #1, then every group's #2, …) so sequential
    /// pairing spreads the group leaders and avoids same-group first-round rematches.
    /// </summary>
    public static (List<Guid> Upper, List<Guid> Lower) SeedPools(IReadOnlyList<IReadOnlyList<Guid>> groupStandings)
    {
        var groupSize = groupStandings[0].Count;
        var half = groupSize / 2;

        var upper = new List<Guid>();
        var lower = new List<Guid>();
        for (var rank = 0; rank < half; rank++)
        {
            foreach (var group in groupStandings)
            {
                upper.Add(group[rank]);
            }
        }

        for (var rank = half; rank < groupSize; rank++)
        {
            foreach (var group in groupStandings)
            {
                lower.Add(group[rank]);
            }
        }

        return (upper, lower);
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
