using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

/// <summary>
/// Projects the authoritative match graph into a round-based bracket view for display.
/// </summary>
internal static class BracketProjection
{
    public static BracketDto Build(Tournament tournament)
    {
        var names = tournament.Participants.ToDictionary(p => p.Id, p => p.Name);

        if (tournament.Type == TournamentType.DoubleElimination)
        {
            return BuildDoubleElimination(tournament, names);
        }

        if (tournament.Type == TournamentType.RoundRobin)
        {
            return BuildRoundRobin(tournament, names);
        }

        var winnerMatches = SegmentMatches(tournament, BracketSegment.Winner);
        var totalRounds = winnerMatches.Count == 0 ? 0 : winnerMatches.Max(m => m.Round);
        var rounds = GroupIntoRounds(winnerMatches, g => RoundTitle(g, totalRounds), names, tournament);

        var thirdPlace = tournament.Matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            WinnerRounds: rounds,
            LoserRounds: Array.Empty<BracketRoundDto>(),
            GrandFinal: null,
            ThirdPlace: thirdPlace is null ? null : ToMatchDto(thirdPlace, names, tournament),
            ThirdPlacePodium: null,
            Standings: Array.Empty<StandingRowDto>());
    }

    /// <summary>
    /// Round Robin has no advancement, so its "bracket" is just a flat list of rounds - reusing
    /// <see cref="BracketDto.WinnerRounds"/> rather than adding a parallel field. Ranking comes from
    /// <see cref="BuildStandings"/> instead of a Final/Third-Place match.
    /// </summary>
    private static BracketDto BuildRoundRobin(Tournament tournament, IReadOnlyDictionary<Guid, string> names)
    {
        var matches = SegmentMatches(tournament, BracketSegment.RoundRobin);
        var rounds = GroupIntoRounds(matches, PlainRoundTitle, names, tournament);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            WinnerRounds: rounds,
            LoserRounds: Array.Empty<BracketRoundDto>(),
            GrandFinal: null,
            ThirdPlace: null,
            ThirdPlacePodium: null,
            Standings: BuildStandings(matches, names));
    }

    /// <summary>
    /// Ranks participants by decided Round Robin matches. Ties are broken by fewer losses, then by
    /// name for a fully deterministic order - the spec defines no head-to-head tiebreaker.
    /// </summary>
    private static IReadOnlyList<StandingRowDto> BuildStandings(List<Match> matches, IReadOnlyDictionary<Guid, string> names)
    {
        var wins = new Dictionary<Guid, int>();
        var losses = new Dictionary<Guid, int>();

        foreach (var match in matches)
        {
            if (match.WinnerId is not { } winnerId || match.ParticipantAId is not { } a || match.ParticipantBId is not { } b)
            {
                continue;
            }

            var loserId = winnerId == a ? b : a;
            wins[winnerId] = wins.GetValueOrDefault(winnerId) + 1;
            losses[loserId] = losses.GetValueOrDefault(loserId) + 1;
        }

        return names
            .Select(kvp => (Id: kvp.Key, Name: kvp.Value, Wins: wins.GetValueOrDefault(kvp.Key), Losses: losses.GetValueOrDefault(kvp.Key)))
            .OrderByDescending(p => p.Wins)
            .ThenBy(p => p.Losses)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select((p, i) => new StandingRowDto(i + 1, p.Id, p.Name, p.Wins + p.Losses, p.Wins, p.Losses))
            .ToList();
    }

    /// <summary>
    /// Double Elimination has no separate Third Place match - <see cref="BracketDto.ThirdPlacePodium"/>
    /// is derived from the Loser Bracket Final's own recorded result instead. Bye-cascade collapse
    /// (see <see cref="Adaminator.Domain.Brackets.DoubleEliminationBracket"/>) can eliminate every
    /// Loser Bracket match for very low participant counts, in which case there is no third place at all.
    /// </summary>
    private static BracketDto BuildDoubleElimination(Tournament tournament, IReadOnlyDictionary<Guid, string> names)
    {
        var winnerMatches = SegmentMatches(tournament, BracketSegment.Winner);
        var totalWinnerRounds = winnerMatches.Count == 0 ? 0 : winnerMatches.Max(m => m.Round);
        var winnerRounds = GroupIntoRounds(winnerMatches, g => RoundTitle(g, totalWinnerRounds), names, tournament);

        var loserMatches = SegmentMatches(tournament, BracketSegment.Loser);
        var loserRounds = GroupIntoRounds(loserMatches, PlainRoundTitle, names, tournament);

        var grandFinal = tournament.Matches.SingleOrDefault(m => m.Segment == BracketSegment.GrandFinal);
        var thirdPlacePodium = ToSlot(tournament.ThirdPlaceParticipantId, names);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            winnerRounds,
            loserRounds,
            grandFinal is null ? null : ToMatchDto(grandFinal, names, tournament),
            ThirdPlace: null,
            thirdPlacePodium,
            Standings: Array.Empty<StandingRowDto>());
    }

    private static List<Match> SegmentMatches(Tournament tournament, BracketSegment segment) =>
        tournament.Matches
            .Where(m => m.Segment == segment)
            .OrderBy(m => m.Round)
            .ThenBy(m => m.IndexInRound)
            .ToList();

    private static IReadOnlyList<BracketRoundDto> GroupIntoRounds(
        List<Match> matches, Func<int, string> title, IReadOnlyDictionary<Guid, string> names, Tournament tournament) =>
        matches
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .Select(g => new BracketRoundDto(
                g.Key,
                title(g.Key),
                g.OrderBy(m => m.IndexInRound).Select(m => ToMatchDto(m, names, tournament)).ToList()))
            .ToList();

    private static BracketMatchDto ToMatchDto(Match match, IReadOnlyDictionary<Guid, string> names, Tournament tournament)
    {
        var entries = match.ScoreEntries
            .OrderBy(e => e.SequenceNumber)
            .Select(e => new ScoreEntryDto(e.SequenceNumber, e.ScoreA, e.ScoreB, e.ParticipantAWon))
            .ToList();

        var aggregateA = entries.Count(e => e.ParticipantAWon);
        var aggregateB = entries.Count - aggregateA;

        return new BracketMatchDto(
            match.Id,
            match.Segment,
            match.Round,
            match.IndexInRound,
            ToSlot(match.ParticipantAId, names),
            ToSlot(match.ParticipantBId, names),
            match.Status,
            match.WinnerId,
            match.MatchFormat,
            match.ScoreType,
            entries,
            aggregateA,
            aggregateB,
            match.CompletedAt,
            CanUndo: tournament.CanUndo(match.Id));
    }

    private static BracketSlotDto? ToSlot(Guid? participantId, IReadOnlyDictionary<Guid, string> names) =>
        participantId is null ? null : new BracketSlotDto(participantId.Value, names.GetValueOrDefault(participantId.Value, "?"));

    /// <summary>Plain "Round N" title, used where rounds never get a Final/Semifinals-style name (Round Robin, the Loser Bracket).</summary>
    private static string PlainRoundTitle(int round) => $"Round {round}";

    private static string RoundTitle(int round, int totalRounds)
    {
        if (totalRounds <= 0)
        {
            return $"Round {round}";
        }

        return (totalRounds - round) switch
        {
            0 => "Final",
            1 => "Semifinals",
            2 => "Quarterfinals",
            _ => $"Round {round}"
        };
    }
}
