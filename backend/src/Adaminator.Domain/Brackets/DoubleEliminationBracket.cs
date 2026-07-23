using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Brackets;

/// <summary>A node in the Double Elimination match graph, identified independently of any Tournament instance.</summary>
public readonly record struct BracketMatchRef(BracketSegment Segment, int Round, int IndexInRound);

/// <summary>A forward route from one match to a specific slot of another.</summary>
public readonly record struct BracketRoute(BracketMatchRef Target, bool SlotA);

/// <summary>One match's routes within the pure (capacity-only, uncollapsed) topology.</summary>
public sealed record TopologyMatch(BracketMatchRef Ref, BracketRoute? WinnerTo, BracketRoute? LoserTo);

/// <summary>
/// Pure Double Elimination bracket math: topology generation (a function of capacity alone,
/// deterministic and snapshot-tested - AC-DE-001/002) and bye-cascade collapse into a concrete,
/// tournament-specific match graph with resolved, persisted routing (see <see cref="Match.SetRoutes"/>).
/// Winner Bracket construction/bye-pairing mirrors <see cref="SingleEliminationBracket"/> exactly
/// (spec section 5); see docs/Adaminator-double-elimination-spec for the full specification.
/// </summary>
public static class DoubleEliminationBracket
{
    /// <summary>There is no 2-slot Double Elimination topology; the smallest supported capacity is 4.</summary>
    public const int MinCapacity = 4;

    /// <summary>The bracket capacities <see cref="GenerateTopology"/> supports.</summary>
    public static readonly IReadOnlyList<int> SupportedCapacities = new[] { 4, 8, 16, 32 };

    public static int ComputeBracketSize(int participantCount) =>
        Math.Max(MinCapacity, SingleEliminationBracket.ComputeBracketSize(participantCount));

    public static int ComputeRequiredByes(int participantCount) =>
        ComputeBracketSize(participantCount) - participantCount;

    public static int WinnerRoundCount(int capacity) => SingleEliminationBracket.RoundCount(capacity);

    public static int LoserRoundCount(int capacity) => 2 * WinnerRoundCount(capacity) - 2;

    /// <summary>
    /// Generates the full match graph topology for a supported capacity, as if every slot were
    /// filled by a real participant (no byes). Pure function of capacity alone: same input always
    /// produces the same output (AC-DE-001), matched against approved snapshots (AC-DE-002).
    /// </summary>
    public static IReadOnlyList<TopologyMatch> GenerateTopology(int capacity)
    {
        ValidateCapacity(capacity);
        var wbRounds = WinnerRoundCount(capacity);

        // Winner Bracket loser destinations, filled in while building the Loser Bracket below,
        // then applied when the Winner Bracket matches are constructed further down.
        var wbLoserDestination = new Dictionary<BracketMatchRef, BracketRoute>();

        var lbMatches = new List<TopologyMatch>();

        // ---- Loser Bracket round 1: fresh Winner Bracket round-1 losers only, paired adjacently.
        // No prior Loser Bracket survivors exist yet, so no rematch is possible - no crossover needed. ----
        var wbRound1Count = capacity >> 1;
        var lbRound1Count = wbRound1Count / 2;
        var survivors = new List<BracketMatchRef>(lbRound1Count);
        for (var i = 0; i < lbRound1Count; i++)
        {
            var lbRef = new BracketMatchRef(BracketSegment.Loser, 1, i);
            survivors.Add(lbRef);
            lbMatches.Add(new TopologyMatch(lbRef, WinnerTo: null, LoserTo: null));

            wbLoserDestination[new BracketMatchRef(BracketSegment.Winner, 1, 2 * i)] = new BracketRoute(lbRef, SlotA: true);
            wbLoserDestination[new BracketMatchRef(BracketSegment.Winner, 1, 2 * i + 1)] = new BracketRoute(lbRef, SlotA: false);
        }

        var lbRoundIndex = 1;

        // ---- Remaining Winner Bracket rounds (2..wbRounds, the last being the Final) drop in here.
        // Each drop-in round pairs the current survivors (index-aligned) against the incoming
        // Winner Bracket losers (reversed index) to reduce immediate rematches (spec section 7):
        // an upper-branch Winner Bracket loser lands against a lower-branch Loser Bracket survivor
        // and vice versa. Every drop-in round after the first is preceded by exactly one pure
        // consolidation round (survivors only, no fresh blood, straightforward adjacent pairing) to
        // halve the survivor count down to the next Winner Bracket round's match count. ----
        for (var m = 2; m <= wbRounds; m++)
        {
            if (m >= 3)
            {
                lbRoundIndex++;
                var consolidated = new List<BracketMatchRef>(survivors.Count / 2);
                for (var i = 0; i < survivors.Count / 2; i++)
                {
                    var lbRef = new BracketMatchRef(BracketSegment.Loser, lbRoundIndex, i);
                    consolidated.Add(lbRef);
                    lbMatches.Add(new TopologyMatch(lbRef, WinnerTo: null, LoserTo: null));

                    LinkWinnerTo(lbMatches, survivors[2 * i], new BracketRoute(lbRef, SlotA: true));
                    LinkWinnerTo(lbMatches, survivors[2 * i + 1], new BracketRoute(lbRef, SlotA: false));
                }

                survivors = consolidated;
            }

            lbRoundIndex++;
            var wbCount = capacity >> m; // 1 when m == wbRounds (the Winner Bracket Final).
            var dropIn = new List<BracketMatchRef>(wbCount);
            for (var i = 0; i < wbCount; i++)
            {
                var lbRef = new BracketMatchRef(BracketSegment.Loser, lbRoundIndex, i);
                dropIn.Add(lbRef);
                lbMatches.Add(new TopologyMatch(lbRef, WinnerTo: null, LoserTo: null));

                LinkWinnerTo(lbMatches, survivors[i], new BracketRoute(lbRef, SlotA: true));

                var wbLoserIndex = wbCount - 1 - i;
                wbLoserDestination[new BracketMatchRef(BracketSegment.Winner, m, wbLoserIndex)] = new BracketRoute(lbRef, SlotA: false);
            }

            survivors = dropIn;
        }

        // survivors now holds exactly the Loser Bracket Final.
        var grandFinalRef = new BracketMatchRef(BracketSegment.GrandFinal, 1, 0);
        LinkWinnerTo(lbMatches, survivors[0], new BracketRoute(grandFinalRef, SlotA: false));

        // ---- Winner Bracket (identical progression to Single Elimination - spec section 5). ----
        var wbMatches = new List<TopologyMatch>();
        for (var r = 1; r <= wbRounds; r++)
        {
            var count = capacity >> r;
            for (var i = 0; i < count; i++)
            {
                var wbRef = new BracketMatchRef(BracketSegment.Winner, r, i);
                var winnerTo = r < wbRounds
                    ? new BracketRoute(new BracketMatchRef(BracketSegment.Winner, r + 1, i / 2), SlotA: i % 2 == 0)
                    : new BracketRoute(grandFinalRef, SlotA: true);

                wbMatches.Add(new TopologyMatch(wbRef, winnerTo, wbLoserDestination[wbRef]));
            }
        }

        var all = new List<TopologyMatch>(wbMatches.Count + lbMatches.Count + 1);
        all.AddRange(wbMatches);
        all.AddRange(lbMatches);
        all.Add(new TopologyMatch(grandFinalRef, WinnerTo: null, LoserTo: null));
        return all;
    }

    /// <summary>
    /// Builds the concrete match graph for a specific tournament: bye pairing/placement mirrors
    /// <see cref="SingleEliminationBracket.BuildMatches"/> exactly for the Winner Bracket, then a
    /// bye-cascade collapse determines which Loser Bracket matches from the pure topology actually
    /// get created for this participant count (any match that would end up with fewer than 2 real
    /// entrants is skipped - no Match row, no win/loss - and its lone real entrant, if any,
    /// auto-advances along that match's own route, recursively). Every created match's
    /// WinnerToMatchId/LoserToMatchId is resolved through any such collapsed hops up front, so
    /// later result entry never needs to re-derive routing (the graph is immutable once built).
    /// </summary>
    public static List<Match> BuildMatches(Tournament tournament)
    {
        var ordered = tournament.Participants.OrderBy(p => p.Seed).ToList();
        var count = ordered.Count;
        var capacity = ComputeBracketSize(count);
        var requiredByes = ComputeRequiredByes(count);
        var pairingCount = capacity / 2;
        var pairings = SingleEliminationBracket.ComputeRound1Pairings(ordered, capacity, requiredByes);

        var topology = GenerateTopology(capacity);
        var topologyByRef = topology.ToDictionary(t => t.Ref);
        var wbRounds = WinnerRoundCount(capacity);
        var lbRounds = LoserRoundCount(capacity);
        var winnerFormat = tournament.PlayoffFormatFor(BracketSegment.Winner);
        var loserFormat = tournament.PlayoffFormatFor(BracketSegment.Loser);
        var grandFinalFormat = tournament.PlayoffFormatFor(BracketSegment.GrandFinal);
        var scoreType = tournament.DefaultScoreType;

        var round1IsReal = new bool[pairingCount];
        for (var i = 0; i < pairingCount; i++)
        {
            round1IsReal[i] = pairings[i].B is not null;
        }

        // Effective real-entrant count (0/1/2) for every Loser Bracket match, via a single forward
        // pass in round order: a Loser round only ever depends on the previous Loser round or a
        // Winner Bracket round, never a later round (spec section 15).
        var incoming = new Dictionary<BracketMatchRef, List<BracketMatchRef>>();
        foreach (var t in topology)
        {
            if (t.WinnerTo is { } w)
            {
                AddIncoming(incoming, w.Target, t.Ref);
            }

            if (t.LoserTo is { } l)
            {
                AddIncoming(incoming, l.Target, t.Ref);
            }
        }

        var loserRefsByRound = topology
            .Where(t => t.Ref.Segment == BracketSegment.Loser)
            .GroupBy(t => t.Ref.Round)
            .ToDictionary(g => g.Key, g => g.Select(t => t.Ref).ToList());

        var realEntrantCount = new Dictionary<BracketMatchRef, int>();

        int Contribution(BracketMatchRef source)
        {
            if (source.Segment == BracketSegment.Winner)
            {
                // Winner Bracket matches always yield a real loser, except a round-1 bye (no match).
                return source.Round == 1 && !round1IsReal[source.IndexInRound] ? 0 : 1;
            }

            return realEntrantCount.TryGetValue(source, out var c) ? Math.Min(1, c) : 0;
        }

        for (var r = 1; r <= lbRounds; r++)
        {
            foreach (var lbRef in loserRefsByRound[r])
            {
                var feeders = incoming.TryGetValue(lbRef, out var list) ? list : new List<BracketMatchRef>();
                realEntrantCount[lbRef] = feeders.Sum(Contribution);
            }
        }

        BracketRoute ResolveRealDestination(BracketRoute route)
        {
            while (route.Target.Segment == BracketSegment.Loser && realEntrantCount.GetValueOrDefault(route.Target) < 2)
            {
                route = topologyByRef[route.Target].WinnerTo
                    ?? throw new DomainException("Double Elimination graph resolution failed to find a real destination.");
            }

            return route;
        }

        // ---- Materialize real Match entities ----
        var byRef = new Dictionary<BracketMatchRef, Match>();

        // Winner Bracket round 1: bye recipients pre-fill round 2 directly (no match for a bye).
        var round2Prefill = new Dictionary<int, (Guid? SlotA, Guid? SlotB)>();
        for (var i = 0; i < pairingCount; i++)
        {
            var (a, b) = pairings[i];
            if (b is not null)
            {
                byRef[new BracketMatchRef(BracketSegment.Winner, 1, i)] =
                    Match.Create(tournament.Id, BracketSegment.Winner, 1, i, a, b, winnerFormat, scoreType);
                continue;
            }

            var targetIndex = i / 2;
            var slotA = i % 2 == 0;
            var current = round2Prefill.TryGetValue(targetIndex, out var existing) ? existing : (null, null);
            if (slotA)
            {
                current.SlotA = a;
            }
            else
            {
                current.SlotB = a;
            }

            round2Prefill[targetIndex] = current;
        }

        // Winner Bracket rounds 2..wbRounds are always fully real.
        for (var r = 2; r <= wbRounds; r++)
        {
            var roundCount = capacity >> r;
            for (var i = 0; i < roundCount; i++)
            {
                Guid? a = null;
                Guid? b = null;
                if (r == 2 && round2Prefill.TryGetValue(i, out var pre))
                {
                    a = pre.SlotA;
                    b = pre.SlotB;
                }

                byRef[new BracketMatchRef(BracketSegment.Winner, r, i)] =
                    Match.Create(tournament.Id, BracketSegment.Winner, r, i, a, b, winnerFormat, scoreType);
            }
        }

        // Loser Bracket matches that end up with exactly 2 real entrants.
        for (var r = 1; r <= lbRounds; r++)
        {
            foreach (var lbRef in loserRefsByRound[r])
            {
                if (realEntrantCount.GetValueOrDefault(lbRef) == 2)
                {
                    byRef[lbRef] = Match.Create(tournament.Id, BracketSegment.Loser, lbRef.Round, lbRef.IndexInRound, null, null, loserFormat, scoreType);
                }
            }
        }

        // Grand Final always exists.
        var grandFinalRef = new BracketMatchRef(BracketSegment.GrandFinal, 1, 0);
        byRef[grandFinalRef] = Match.Create(tournament.Id, BracketSegment.GrandFinal, 1, 0, null, null, grandFinalFormat, scoreType);

        ApplyRoutes(byRef, topologyByRef, ResolveRealDestination);

        return byRef.Values.ToList();
    }

    /// <summary>
    /// Resolves and persists every materialized match's forward routes from the topology. Shared with
    /// <see cref="GroupStagePlayoffBracket"/>, which materializes a subset of the same topology (no
    /// Winner round 1) and needs no hop. <paramref name="resolveHop"/> lets Double Elimination redirect
    /// a route through matches its bye cascade collapsed away; omit it when every target is real.
    /// </summary>
    internal static void ApplyRoutes(
        IReadOnlyDictionary<BracketMatchRef, Match> byRef,
        IReadOnlyDictionary<BracketMatchRef, TopologyMatch> topologyByRef,
        Func<BracketRoute, BracketRoute>? resolveHop = null)
    {
        (Guid? Id, bool? SlotA) Resolve(BracketRoute? route)
        {
            if (route is not { } r)
            {
                return (null, null);
            }

            var resolved = resolveHop is null ? r : resolveHop(r);
            if (!byRef.TryGetValue(resolved.Target, out var target))
            {
                // Every route must land on a materialized match; the only skipped refs (Winner round 1
                // for Group Stage + Playoff) are never anyone's target.
                throw new DomainException($"Bracket route points at a match that was not created: {resolved.Target}.");
            }

            return (target.Id, resolved.SlotA);
        }

        foreach (var (matchRef, match) in byRef)
        {
            var topologyMatch = topologyByRef[matchRef];
            var (winnerToId, winnerToSlotA) = Resolve(topologyMatch.WinnerTo);
            // Only Winner Bracket matches route a loser onward; the topology leaves LoserTo null elsewhere.
            var (loserToId, loserToSlotA) = Resolve(topologyMatch.LoserTo);

            match.SetRoutes(winnerToId, winnerToSlotA, loserToId, loserToSlotA);
        }
    }

    private static void LinkWinnerTo(List<TopologyMatch> matches, BracketMatchRef sourceRef, BracketRoute winnerTo)
    {
        var index = matches.FindIndex(m => m.Ref == sourceRef);
        matches[index] = matches[index] with { WinnerTo = winnerTo };
    }

    private static void AddIncoming(Dictionary<BracketMatchRef, List<BracketMatchRef>> incoming, BracketMatchRef target, BracketMatchRef source)
    {
        if (!incoming.TryGetValue(target, out var list))
        {
            list = new List<BracketMatchRef>();
            incoming[target] = list;
        }

        list.Add(source);
    }

    private static void ValidateCapacity(int capacity)
    {
        if (!SupportedCapacities.Contains(capacity))
        {
            throw new DomainException($"Double Elimination supports only {string.Join(", ", SupportedCapacities)} slot brackets.");
        }
    }
}
