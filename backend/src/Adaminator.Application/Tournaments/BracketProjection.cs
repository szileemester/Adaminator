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

        var winnerMatches = tournament.Matches
            .Where(m => m.Segment == BracketSegment.Winner)
            .OrderBy(m => m.Round)
            .ThenBy(m => m.IndexInRound)
            .ToList();

        var totalRounds = winnerMatches.Count == 0 ? 0 : winnerMatches.Max(m => m.Round);

        var latestCompletionSequence = tournament.Matches
            .Where(m => m.CompletionSequence.HasValue)
            .Select(m => m.CompletionSequence!.Value)
            .DefaultIfEmpty(long.MinValue)
            .Max();

        var rounds = winnerMatches
            .GroupBy(m => m.Round)
            .OrderBy(g => g.Key)
            .Select(g => new BracketRoundDto(
                g.Key,
                RoundTitle(g.Key, totalRounds),
                g.OrderBy(m => m.IndexInRound).Select(m => ToMatchDto(m, names, latestCompletionSequence)).ToList()))
            .ToList();

        var thirdPlace = tournament.Matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);

        return new BracketDto(
            tournament.Type,
            tournament.Status,
            rounds,
            thirdPlace is null ? null : ToMatchDto(thirdPlace, names, latestCompletionSequence));
    }

    private static BracketMatchDto ToMatchDto(Match match, IReadOnlyDictionary<Guid, string> names, long latestCompletionSequence)
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
            CanUndo: match.CompletionSequence == latestCompletionSequence);
    }

    private static BracketSlotDto? ToSlot(Guid? participantId, IReadOnlyDictionary<Guid, string> names) =>
        participantId is null ? null : new BracketSlotDto(participantId.Value, names.GetValueOrDefault(participantId.Value, "?"));

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
