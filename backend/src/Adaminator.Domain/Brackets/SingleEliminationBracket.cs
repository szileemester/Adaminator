using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Brackets;

/// <summary>
/// Pure Single Elimination bracket math and match-graph construction.
///
/// Layout rule (MVP): participants are ordered by seed; bye recipients occupy the top first-round
/// pairings (one participant, no opponent) and advance directly into round 2, and the remaining
/// players fill the rest of the round in seed order. The admin controls who receives a bye and the
/// relative order of the players; bye pairings are always placed at the top of the bracket.
/// </summary>
public static class SingleEliminationBracket
{
    public static int ComputeBracketSize(int participantCount)
    {
        if (participantCount < 2)
        {
            throw new DomainException("A bracket needs at least 2 participants.");
        }

        var size = 1;
        while (size < participantCount)
        {
            size <<= 1;
        }

        return size;
    }

    public static int ComputeRequiredByes(int participantCount) =>
        ComputeBracketSize(participantCount) - participantCount;

    public static int RoundCount(int bracketSize)
    {
        var rounds = 0;
        while (1 << rounds < bracketSize)
        {
            rounds++;
        }

        return rounds;
    }

    /// <summary>
    /// Builds the complete Single Elimination match graph from the tournament's seeded participants.
    /// Round-1 bye pairings do not create a match; instead the bye recipient is pre-placed into its
    /// round-2 slot (automatic advancement). Produces exactly (participants - 1) winner-bracket
    /// matches, plus an optional Third Place match when two semifinals exist.
    /// </summary>
    public static List<Match> BuildMatches(Tournament tournament)
    {
        var ordered = tournament.Participants.OrderBy(p => p.Seed).ToList();
        var count = ordered.Count;
        var size = ComputeBracketSize(count);
        var requiredByes = ComputeRequiredByes(count);

        var byes = ordered.Where(p => p.HasBye).ToList();
        if (byes.Count != requiredByes)
        {
            throw new DomainException($"Exactly {requiredByes} bye(s) must be selected; {byes.Count} chosen.");
        }

        var players = ordered.Where(p => !p.HasBye).ToList();
        var pairingCount = size / 2;
        var pairings = new (Guid? A, Guid? B)[pairingCount];

        for (var k = 0; k < requiredByes; k++)
        {
            pairings[k] = (byes[k].Id, null);
        }

        var playerIndex = 0;
        for (var k = requiredByes; k < pairingCount; k++)
        {
            pairings[k] = (players[playerIndex].Id, players[playerIndex + 1].Id);
            playerIndex += 2;
        }

        var rounds = RoundCount(size);
        var format = tournament.DefaultMatchFormat;
        var matches = new List<Match>();

        // Bye recipients advance into these round-2 slots.
        var round2Prefill = new Dictionary<int, (Guid? Slot0, Guid? Slot1)>();

        for (var k = 0; k < pairingCount; k++)
        {
            var (a, b) = pairings[k];
            if (a is not null && b is not null)
            {
                matches.Add(Match.Create(tournament.Id, BracketSegment.Winner, 1, k, a, b, format));
                continue;
            }

            // Bye pairing: slot A holds the participant, slot B is empty -> advance to round 2.
            var targetMatch = k / 2;
            var slot = k % 2;
            var current = round2Prefill.TryGetValue(targetMatch, out var existing) ? existing : (null, null);
            if (slot == 0)
            {
                current.Item1 = a;
            }
            else
            {
                current.Item2 = a;
            }

            round2Prefill[targetMatch] = current;
        }

        for (var r = 2; r <= rounds; r++)
        {
            var matchesInRound = size >> r;
            for (var m = 0; m < matchesInRound; m++)
            {
                Guid? a = null;
                Guid? b = null;
                if (r == 2 && round2Prefill.TryGetValue(m, out var pre))
                {
                    a = pre.Slot0;
                    b = pre.Slot1;
                }

                matches.Add(Match.Create(tournament.Id, BracketSegment.Winner, r, m, a, b, format));
            }
        }

        if (tournament.ThirdPlaceEnabled && HasTwoSemifinals(rounds, matches))
        {
            matches.Add(Match.Create(tournament.Id, BracketSegment.ThirdPlace, rounds, 0, null, null, format));
        }

        return matches;
    }

    private static bool HasTwoSemifinals(int rounds, List<Match> matches)
    {
        if (rounds < 2)
        {
            return false;
        }

        // Semifinals are the round before the final. For 3+ round brackets that round is always
        // fully created; for a 2-round bracket it is round 1, which byes may have reduced.
        var semifinalRound = rounds - 1;
        return rounds >= 3 || matches.Count(m => m.Segment == BracketSegment.Winner && m.Round == semifinalRound) == 2;
    }
}
