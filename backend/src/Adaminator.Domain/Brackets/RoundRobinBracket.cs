using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;

namespace Adaminator.Domain.Brackets;

/// <summary>
/// Pure Round Robin scheduling: every participant plays every other participant exactly once.
/// Unlike Single/Double Elimination, matches never route into one another - the schedule is a flat
/// list of rounds, and there is no admin-chosen bye (an odd participant count gives each participant
/// exactly one automatic sit-out round via the scheduling algorithm itself).
/// </summary>
public static class RoundRobinBracket
{
    /// <summary>Round Robin has no admin-chosen byes; odd counts are handled per round by <see cref="BuildMatches"/>.</summary>
    public static int ComputeRequiredByes(int participantCount) => 0;

    /// <summary>N-1 rounds for an even participant count, N rounds (one sit-out each) for an odd one.</summary>
    public static int RoundCount(int participantCount)
    {
        if (participantCount < 2)
        {
            return 0;
        }

        return participantCount % 2 == 0 ? participantCount - 1 : participantCount;
    }

    /// <summary>Builds one flat round-robin over the tournament's whole seeded roster (plain Round Robin).</summary>
    public static List<Match> BuildMatches(Tournament tournament)
    {
        var orderedIds = tournament.Participants.OrderBy(p => p.Seed).Select(p => p.Id).ToList();
        return Schedule(tournament.Id, orderedIds, tournament.DefaultMatchFormat, tournament.DefaultScoreType, groupIndex: null);
    }

    /// <summary>
    /// Builds a round-robin schedule over <paramref name="orderedIds"/> using the standard "circle
    /// method": an odd count gets one virtual empty slot appended so the rotation math stays uniform;
    /// position 0 stays fixed and the remaining positions rotate by one after each round; each round
    /// pairs position i with position (n-1-i). Whichever real participant lands opposite the virtual
    /// empty slot sits out that round - no <see cref="Match"/> row is created for it, mirroring how
    /// Single/Double Elimination create no match for a bye pairing. Shared by plain Round Robin, the
    /// Group Stage + Playoff per-group scheduling (which tags each match with its group via
    /// <paramref name="groupIndex"/>), and the tie-breaker mini round-robins (which pass
    /// <paramref name="segment"/> <see cref="BracketSegment.Tiebreaker"/>).
    /// </summary>
    public static List<Match> Schedule(
        Guid tournamentId,
        IReadOnlyList<Guid> orderedIds,
        MatchFormat format,
        ScoreType scoreType,
        int? groupIndex,
        BracketSegment segment = BracketSegment.RoundRobin,
        int roundOffset = 0)
    {
        var slots = orderedIds.Select(id => (Guid?)id).ToList();
        if (slots.Count % 2 != 0)
        {
            slots.Add(null);
        }

        var n = slots.Count;
        var rounds = n - 1;
        var matches = new List<Match>();

        for (var round = 1; round <= rounds; round++)
        {
            var indexInRound = 0;
            for (var i = 0; i < n / 2; i++)
            {
                var a = slots[i];
                var b = slots[n - 1 - i];
                if (a is not null && b is not null)
                {
                    matches.Add(Match.Create(tournamentId, segment, round + roundOffset, indexInRound, a, b, format, scoreType, groupIndex));
                    indexInRound++;
                }
            }

            // Rotate every position but the first one step, wrapping the last into position 1.
            var last = slots[n - 1];
            for (var i = n - 1; i > 1; i--)
            {
                slots[i] = slots[i - 1];
            }

            slots[1] = last;
        }

        return matches;
    }
}
