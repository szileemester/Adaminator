using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;

namespace Adaminator.Domain.Brackets;

/// <summary>A participant's win-loss record within a round-robin (or one group of a group stage).</summary>
public readonly record struct RoundRobinStanding(Guid ParticipantId, int Wins, int Losses)
{
    public int Played => Wins + Losses;
}

/// <summary>
/// Pure round-robin ranking, the single source of truth for both the displayed standings and the
/// Group Stage + Playoff seeding. Within a set of participants tied on the raw win-loss record it
/// applies, in order: any played <see cref="BracketSegment.Tiebreaker"/> match record among them,
/// then head-to-head (round-robin results among them), then game differential, and finally name
/// (ordinal, case-insensitive) as a deterministic backstop - names are unique within a tournament,
/// so the order is always total.
/// </summary>
public static class RoundRobinStandings
{
    /// <summary>
    /// Ranks <paramref name="participants"/> by their record in <paramref name="matches"/>. The match
    /// set may include both <see cref="BracketSegment.RoundRobin"/> and <see cref="BracketSegment.Tiebreaker"/>
    /// matches for the scope (one group, or a whole round-robin field); they are separated by segment
    /// here. <paramref name="roster"/> supplies the id-to-participant lookup (used for the name tiebreak).
    /// </summary>
    public static List<RoundRobinStanding> Rank(
        IEnumerable<Match> matches, IReadOnlyCollection<Participant> participants, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var (standingById, atoms) = Analyse(matches, participants, roster, tiers: AllTiers);

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

    /// <summary>Every automatic criterion, best-first, applied only within a cohort already level on wins and losses.</summary>
    private const int AllTiers = 3;

    /// <summary>Only the played tie-breaker record - what <see cref="TiebreakerPolicy.AlwaysMatch"/> allows to separate a cohort without playing.</summary>
    private const int TiebreakerRecordOnly = 1;

    /// <summary>
    /// The shared core: the win-loss record per participant, plus the record cohorts refined into
    /// "atoms" - runs the automatic criteria cannot separate - in final standings order (bar the name
    /// tiebreak). Both public entry points read the same pass rather than each deriving it again.
    /// </summary>
    private static (Dictionary<Guid, RoundRobinStanding> StandingById, List<List<Guid>> Atoms) Analyse(
        IEnumerable<Match> matches, IReadOnlyCollection<Participant> participants, IReadOnlyDictionary<Guid, Participant> roster, int tiers)
    {
        var (rr, tb) = SplitSegments(matches);
        var (wins, losses) = WinLoss(rr);
        var gameDiff = GameDifferential(rr, participants);

        var standingById = participants.ToDictionary(
            p => p.Id, p => new RoundRobinStanding(p.Id, wins.GetValueOrDefault(p.Id), losses.GetValueOrDefault(p.Id)));

        var atoms = new List<List<Guid>>();
        foreach (var cohort in RecordCohorts(standingById.Values))
        {
            atoms.AddRange(AutoAtoms(cohort, rr, tb, gameDiff, tiers));
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
        IReadOnlyCollection<int> boundaryCuts)
    {
        // AlwaysMatch deliberately ignores head-to-head/differential when deciding *whether* to play, so
        // only the played tie-breaker record may separate a cohort for it; ComputedThenMatch may use all
        // three. Either way, anything still lumped together needs (another) round.
        var tiers = policy == TiebreakerPolicy.AlwaysMatch ? TiebreakerRecordOnly : AllTiers;
        var (_, atoms) = Analyse(matches, participants, roster, tiers);

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

    private static (Dictionary<Guid, int> Wins, Dictionary<Guid, int> Losses) WinLoss(IEnumerable<Match> matches)
    {
        var wins = new Dictionary<Guid, int>();
        var losses = new Dictionary<Guid, int>();
        foreach (var match in matches)
        {
            if (match.WinnerId is not { } winnerId || match.LoserId is not { } loserId)
            {
                continue;
            }

            wins[winnerId] = wins.GetValueOrDefault(winnerId) + 1;
            losses[loserId] = losses.GetValueOrDefault(loserId) + 1;
        }

        return (wins, losses);
    }

    /// <summary>Game differential (games won minus games lost) over played games; forfeits with no recorded games contribute 0.</summary>
    private static Dictionary<Guid, int> GameDifferential(IEnumerable<Match> roundRobin, IReadOnlyCollection<Participant> participants)
    {
        var diff = participants.ToDictionary(p => p.Id, _ => 0);
        foreach (var match in roundRobin)
        {
            if (match.ParticipantAId is not { } a || match.ParticipantBId is not { } b)
            {
                continue;
            }

            var aGames = match.ScoreEntries.Count(e => e.ParticipantAWon);
            var bGames = match.ScoreEntries.Count - aGames;
            if (diff.ContainsKey(a))
            {
                diff[a] += aGames - bGames;
            }

            if (diff.ContainsKey(b))
            {
                diff[b] += bGames - aGames;
            }
        }

        return diff;
    }

    /// <summary>Groups standings into cohorts of an equal (wins, losses) record, ordered wins desc then losses asc.</summary>
    private static List<List<Guid>> RecordCohorts(IEnumerable<RoundRobinStanding> standings) =>
        standings
            .GroupBy(s => (s.Wins, s.Losses))
            .OrderByDescending(g => g.Key.Wins)
            .ThenBy(g => g.Key.Losses)
            .Select(g => g.Select(s => s.ParticipantId).ToList())
            .ToList();

    /// <summary>
    /// Splits a record-cohort into ordered "atoms" - maximal subsets that the automatic criteria
    /// (tiebreaker-match record, then head-to-head, then game differential) cannot separate. Members
    /// within one atom are distinguishable only by name. Atoms are returned best-first.
    /// </summary>
    /// <param name="tiers">How many of the criteria may separate the cohort: 1 = the played tie-breaker record only, 3 = all of them.</param>
    private static List<List<Guid>> AutoAtoms(
        List<Guid> members, List<Match> rr, List<Match> tb, IReadOnlyDictionary<Guid, int> gameDiff, int tiers)
    {
        if (members.Count <= 1)
        {
            return new List<List<Guid>> { members };
        }

        // Best-first: the played tie-breaker record, then head-to-head within the cohort, then game
        // differential over the whole scope. The first that separates anyone wins; the rest recurse.
        var criteria = new Func<List<Guid>, IReadOnlyDictionary<Guid, int>>[]
        {
            cohort => WinsAmong(cohort, tb),
            cohort => WinsAmong(cohort, rr),
            cohort => cohort.ToDictionary(id => id, id => gameDiff.GetValueOrDefault(id)),
        };

        foreach (var score in criteria.Take(tiers))
        {
            var split = SplitByScore(members, score(members));
            if (split.Count > 1)
            {
                return split.SelectMany(group => AutoAtoms(group, rr, tb, gameDiff, tiers)).ToList();
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
