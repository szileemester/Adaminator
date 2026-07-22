using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;

namespace Adaminator.Domain.Brackets;

/// <summary>A participant's record within a round-robin (or one group of a group stage): matches played/won/lost and total games won/lost.</summary>
public readonly record struct RoundRobinStanding(Guid ParticipantId, int Played, int Wins, int Losses, int GamesWon, int GamesLost);

/// <summary>
/// Pure round-robin ranking, the single source of truth for both the displayed standings and the
/// Group Stage + Playoff seeding. The primary key is match wins, or - for a Best-of-2 group
/// (<paramref name="byGamesWon"/>) - total games won. Within a set tied on that record it applies, in
/// order: any played <see cref="BracketSegment.Tiebreaker"/> record among them, then head-to-head
/// (match wins, or games, among them), then game differential, and finally name (ordinal,
/// case-insensitive) as a deterministic backstop - names are unique within a tournament, so the order
/// is always total.
/// </summary>
public static class RoundRobinStandings
{
    /// <summary>
    /// Ranks <paramref name="participants"/> by their record in <paramref name="matches"/> (which may mix
    /// <see cref="BracketSegment.RoundRobin"/> and <see cref="BracketSegment.Tiebreaker"/> matches for the
    /// scope; they are separated by segment here). Pass <paramref name="byGamesWon"/> true to rank a
    /// Best-of-2 group by total games won rather than match wins.
    /// </summary>
    public static List<RoundRobinStanding> Rank(
        IEnumerable<Match> matches,
        IReadOnlyCollection<Participant> participants,
        IReadOnlyDictionary<Guid, Participant> roster,
        bool byGamesWon = false)
    {
        var (standingById, atoms) = Analyse(matches, participants, byGamesWon, tiers: AllTiers);

        var ordered = new List<RoundRobinStanding>(participants.Count);
        foreach (var atom in atoms)
        {
            foreach (var id in atom.OrderBy(id => roster[id].Name, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(standingById[id]);
            }
        }

        return ordered;
    }

    /// <summary>Every automatic criterion, best-first, applied only within a cohort already level on the primary record.</summary>
    private const int AllTiers = 3;

    /// <summary>Only the played tie-breaker record - what <see cref="TiebreakerPolicy.AlwaysMatch"/> allows to separate a cohort without playing.</summary>
    private const int TiebreakerRecordOnly = 1;

    /// <summary>
    /// The shared core: the record per participant, plus the record cohorts refined into "atoms" - runs
    /// the automatic criteria cannot separate - in final standings order (bar the name tiebreak). Both
    /// public entry points read the same pass rather than each deriving it again.
    /// </summary>
    private static (Dictionary<Guid, RoundRobinStanding> StandingById, List<List<Guid>> Atoms) Analyse(
        IEnumerable<Match> matches, IReadOnlyCollection<Participant> participants, bool byGamesWon, int tiers)
    {
        var (rr, tb) = SplitSegments(matches);
        var standingById = Aggregate(rr, participants);

        var atoms = new List<List<Guid>>();
        foreach (var cohort in RecordCohorts(standingById.Values, byGamesWon))
        {
            atoms.AddRange(AutoAtoms(cohort, rr, tb, standingById, byGamesWon, tiers));
        }

        return (standingById, atoms);
    }

    /// <summary>
    /// Finds the cohorts of tied participants that (a) cannot be separated by this <paramref name="policy"/>'s
    /// automatic criteria <em>together with every tie-breaker match played so far</em>, and (b) straddle a
    /// decision boundary (a cut index in <paramref name="boundaryCuts"/> falling strictly inside the cohort's
    /// contiguous span of the tentative order). Each returned cohort is ordered by the tentative standings.
    /// <para>
    /// Generation repeats: a tie-breaker round that itself ends in a cycle leaves the cohort unresolved, so
    /// this keeps returning it and another round is played. Only real results end the stage - the name
    /// ordering in <see cref="Rank"/> is a display backstop, never a reason to stop playing.
    /// </para>
    /// </summary>
    public static List<IReadOnlyList<Guid>> FindUnresolvedTieCohorts(
        IEnumerable<Match> matches,
        IReadOnlyCollection<Participant> participants,
        IReadOnlyDictionary<Guid, Participant> roster,
        TiebreakerPolicy policy,
        IReadOnlyCollection<int> boundaryCuts,
        bool byGamesWon = false)
    {
        // AlwaysMatch deliberately ignores head-to-head/differential when deciding *whether* to play, so
        // only the played tie-breaker record may separate a cohort for it; ComputedThenMatch may use all
        // three. Either way, anything still lumped together needs (another) round.
        var tiers = policy == TiebreakerPolicy.AlwaysMatch ? TiebreakerRecordOnly : AllTiers;
        var (_, atoms) = Analyse(matches, participants, byGamesWon, tiers);

        var position = Positions(atoms, roster);
        return atoms
            .Where(atom => atom.Count >= 2 && Straddles(atom, position, boundaryCuts))
            .Select(atom => (IReadOnlyList<Guid>)atom.OrderBy(id => position[id]).ToList())
            .ToList();
    }

    /// <summary>Final standings index of every participant, applying the same name backstop <see cref="Rank"/> uses so positions line up with the displayed order.</summary>
    private static Dictionary<Guid, int> Positions(List<List<Guid>> atoms, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var position = new Dictionary<Guid, int>();
        foreach (var id in atoms.SelectMany(atom => atom.OrderBy(id => roster[id].Name, StringComparer.OrdinalIgnoreCase)))
        {
            position[id] = position.Count;
        }

        return position;
    }

    private static (List<Match> RoundRobin, List<Match> Tiebreaker) SplitSegments(IEnumerable<Match> matches)
    {
        var rr = new List<Match>();
        var tb = new List<Match>();
        foreach (var match in matches)
        {
            if (match.Segment == BracketSegment.RoundRobin)
            {
                rr.Add(match);
            }
            else if (match.Segment == BracketSegment.Tiebreaker)
            {
                tb.Add(match);
            }
        }

        return (rr, tb);
    }

    /// <summary>
    /// Per-participant record over the decided matches: matches played, match wins/losses (a drawn
    /// Best-of-2 counts as played but is neither), and total games won/lost from the recorded games.
    /// </summary>
    private static Dictionary<Guid, RoundRobinStanding> Aggregate(IEnumerable<Match> roundRobin, IReadOnlyCollection<Participant> participants)
    {
        var played = participants.ToDictionary(p => p.Id, _ => 0);
        var wins = participants.ToDictionary(p => p.Id, _ => 0);
        var losses = participants.ToDictionary(p => p.Id, _ => 0);
        var gamesWon = participants.ToDictionary(p => p.Id, _ => 0);
        var gamesLost = participants.ToDictionary(p => p.Id, _ => 0);

        foreach (var match in roundRobin)
        {
            if (!match.IsDecided || match.ParticipantAId is not { } a || match.ParticipantBId is not { } b
                || !played.ContainsKey(a) || !played.ContainsKey(b))
            {
                continue;
            }

            played[a]++;
            played[b]++;

            var (aGames, bGames) = GamesEach(match);
            gamesWon[a] += aGames;
            gamesLost[a] += bGames;
            gamesWon[b] += bGames;
            gamesLost[b] += aGames;

            if (match.WinnerId is { } winnerId && match.LoserId is { } loserId)
            {
                wins[winnerId]++;
                losses[loserId]++;
            }
        }

        return participants.ToDictionary(
            p => p.Id,
            p => new RoundRobinStanding(p.Id, played[p.Id], wins[p.Id], losses[p.Id], gamesWon[p.Id], gamesLost[p.Id]));
    }

    /// <summary>Games won by each side of a decided match; a drawn Best-of-2 still splits its games (e.g. 1-1).</summary>
    private static (int A, int B) GamesEach(Match match)
    {
        var aGames = match.ScoreEntries.Count(e => e.ParticipantAWon);
        return (aGames, match.ScoreEntries.Count - aGames);
    }

    /// <summary>Groups standings into cohorts of an equal primary record - match wins, or games won for a Best-of-2 group - best-first, with fewer losses breaking equal-primary ties.</summary>
    private static List<List<Guid>> RecordCohorts(IEnumerable<RoundRobinStanding> standings, bool byGamesWon)
    {
        var key = byGamesWon
            ? (Func<RoundRobinStanding, (int Primary, int Secondary)>)(s => (s.GamesWon, s.GamesLost))
            : s => (s.Wins, s.Losses);

        return standings
            .GroupBy(key)
            .OrderByDescending(g => g.Key.Primary)
            .ThenBy(g => g.Key.Secondary)
            .Select(g => g.Select(s => s.ParticipantId).ToList())
            .ToList();
    }

    /// <summary>
    /// Splits a record-cohort into ordered "atoms" - maximal subsets that the automatic criteria
    /// (tiebreaker-match record, then head-to-head, then game differential) cannot separate. Members
    /// within one atom are distinguishable only by name. Atoms are returned best-first.
    /// </summary>
    /// <param name="tiers">How many of the criteria may separate the cohort: 1 = the played tie-breaker record only, 3 = all of them.</param>
    private static List<List<Guid>> AutoAtoms(
        List<Guid> members, List<Match> rr, List<Match> tb, IReadOnlyDictionary<Guid, RoundRobinStanding> standingById, bool byGamesWon, int tiers)
    {
        if (members.Count <= 1)
        {
            return new List<List<Guid>> { members };
        }

        // Best-first: the played tie-breaker record (always decisive), then head-to-head within the
        // cohort (match wins, or games for a Best-of-2 group), then overall game differential. The first
        // that separates anyone wins; the rest recurse.
        var criteria = new Func<List<Guid>, IReadOnlyDictionary<Guid, int>>[]
        {
            cohort => WinsAmong(cohort, tb),
            cohort => byGamesWon ? GamesAmong(cohort, rr) : WinsAmong(cohort, rr),
            cohort => cohort.ToDictionary(id => id, id => standingById[id].GamesWon - standingById[id].GamesLost),
        };

        foreach (var score in criteria.Take(tiers))
        {
            var split = SplitByScore(members, score(members));
            if (split.Count > 1)
            {
                return split.SelectMany(group => AutoAtoms(group, rr, tb, standingById, byGamesWon, tiers)).ToList();
            }
        }

        // Indistinguishable by any automatic criterion - one atom.
        return new List<List<Guid>> { members };
    }

    /// <summary>Wins each member has in <paramref name="matches"/> counting only matches played between two members of the set.</summary>
    internal static Dictionary<Guid, int> WinsAmong(List<Guid> members, IEnumerable<Match> matches)
    {
        var set = members.ToHashSet();
        var wins = members.ToDictionary(id => id, _ => 0);
        foreach (var match in matches)
        {
            if (match.WinnerId is { } winnerId && match.LoserId is { } loserId && set.Contains(winnerId) && set.Contains(loserId))
            {
                wins[winnerId]++;
            }
        }

        return wins;
    }

    /// <summary>Games each member won counting only matches played between two members of the set (the games head-to-head for a Best-of-2 group).</summary>
    private static Dictionary<Guid, int> GamesAmong(List<Guid> members, IEnumerable<Match> matches)
    {
        var set = members.ToHashSet();
        var games = members.ToDictionary(id => id, _ => 0);
        foreach (var match in matches)
        {
            if (match.ParticipantAId is { } a && match.ParticipantBId is { } b && set.Contains(a) && set.Contains(b))
            {
                var (aGames, bGames) = GamesEach(match);
                games[a] += aGames;
                games[b] += bGames;
            }
        }

        return games;
    }

    /// <summary>Partitions members by a per-member score into groups ordered by score descending; a single group means "all equal".</summary>
    internal static List<List<Guid>> SplitByScore(List<Guid> members, IReadOnlyDictionary<Guid, int> score) =>
        members
            .GroupBy(id => score[id])
            .OrderByDescending(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

    private static bool Straddles(List<Guid> cohort, IReadOnlyDictionary<Guid, int> position, IReadOnlyCollection<int> boundaryCuts)
    {
        var lo = cohort.Min(id => position[id]);
        var hi = cohort.Max(id => position[id]);
        return boundaryCuts.Any(cut => GroupStagePlayoffBracket.SpansCut(lo, hi, cut));
    }
}
