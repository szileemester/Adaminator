using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Brackets;

/// <summary>
/// Pure Swiss pairing over one single pool - the Group Stage + Playoff alternative to per-group round
/// robins. Unlike every other builder here, a Swiss schedule cannot be produced up front: round N+1's
/// pairings depend on round N's results, so <see cref="BuildRound"/> is called once per round as the
/// admin advances the stage.
///
/// Everything the pairing needs (who has met whom, who has already had a bye) is derived from the
/// existing <see cref="Match"/> rows, so Swiss adds no persisted state of its own.
/// </summary>
public static class SwissBracket
{
    /// <summary>
    /// The conventional round count: enough rounds for one undefeated participant to remain, i.e.
    /// ceil(log2(n)). 8 players -> 3 rounds, 9 -> 4, 16 -> 4.
    /// </summary>
    public static int DefaultRounds(int participantCount)
    {
        if (participantCount < 2)
        {
            return 0;
        }

        var rounds = 0;
        var reach = 1;
        while (reach < participantCount)
        {
            reach <<= 1;
            rounds++;
        }

        return rounds;
    }

    /// <summary>The most rounds worth playing - beyond a full round robin there are no fresh pairings left.</summary>
    public static int MaxRounds(int participantCount) => Math.Max(1, participantCount - 1);

    /// <summary>Validates the Swiss shape; thrown at tournament start, mirroring the other builders' start-time validation.</summary>
    public static void ValidateShape(int participantCount, int rounds)
    {
        if (participantCount < 2)
        {
            throw new DomainException($"A Swiss group stage needs at least 2 participants; {participantCount} given.");
        }

        if (rounds < 1)
        {
            throw new DomainException("A Swiss group stage needs at least 1 round.");
        }

        var max = MaxRounds(participantCount);
        if (rounds > max)
        {
            throw new DomainException(
                $"{participantCount} participants support at most {max} Swiss round(s); {rounds} requested.");
        }
    }

    /// <summary>
    /// Builds one Swiss round. <paramref name="standingOrder"/> is the current standings (best first)
    /// for round 2 onward, and the seeded roster order for round 1.
    ///
    /// Pairing walks that order top-down and pairs each still-unpaired participant with the next
    /// unpaired one they have not already met, so equal records meet each other. If a participant has
    /// already met everyone left, the next available opponent is taken anyway - a repeat pairing is
    /// better than leaving the round unplayable.
    /// </summary>
    public static List<Match> BuildRound(Tournament tournament, int round, IReadOnlyList<Guid> standingOrder)
    {
        var format = tournament.GroupStageMatchFormat;
        var scoreType = tournament.DefaultScoreType;
        var played = PriorMeetings(tournament.Matches);
        var pool = standingOrder.ToList();
        var matches = new List<Match>();
        var indexInRound = 0;

        // An odd pool leaves one participant over: the lowest-ranked who has not had a bye yet gets
        // the free win (and if everyone has, the lowest-ranked overall).
        if (pool.Count % 2 != 0)
        {
            var alreadyByed = ByeRecipients(tournament.Matches);
            var byeIndex = pool.FindLastIndex(id => !alreadyByed.Contains(id));
            if (byeIndex < 0)
            {
                byeIndex = pool.Count - 1;
            }

            matches.Add(Match.CreateBye(tournament.Id, round, indexInRound++, pool[byeIndex], format, scoreType));
            pool.RemoveAt(byeIndex);
        }

        while (pool.Count > 0)
        {
            var a = pool[0];
            pool.RemoveAt(0);

            var opponent = pool.FindIndex(b => !played.Contains(PairKey(a, b)));
            if (opponent < 0)
            {
                opponent = 0;
            }

            var b = pool[opponent];
            pool.RemoveAt(opponent);

            matches.Add(Match.Create(
                tournament.Id, BracketSegment.RoundRobin, round, indexInRound++, a, b, format, scoreType, groupIndex: null));
        }

        return matches;
    }

    /// <summary>Every pairing already scheduled in the Swiss stage, as unordered participant pairs.</summary>
    private static HashSet<(Guid, Guid)> PriorMeetings(IEnumerable<Match> matches)
    {
        var met = new HashSet<(Guid, Guid)>();
        foreach (var match in matches)
        {
            if (match.Segment == BracketSegment.RoundRobin
                && match.ParticipantAId is { } a
                && match.ParticipantBId is { } b)
            {
                met.Add(PairKey(a, b));
            }
        }

        return met;
    }

    /// <summary>Participants who have already had a bye - each may only ever receive one.</summary>
    private static HashSet<Guid> ByeRecipients(IEnumerable<Match> matches) =>
        matches.Where(m => m.IsBye).Select(m => m.ParticipantAId!.Value).ToHashSet();

    /// <summary>Order-independent key for a pairing, so A-v-B and B-v-A are the same meeting.</summary>
    private static (Guid, Guid) PairKey(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);
}
