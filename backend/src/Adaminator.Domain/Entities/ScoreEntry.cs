namespace Adaminator.Domain.Entities;

/// <summary>One recorded game/set result within a <see cref="Match"/>.</summary>
public class ScoreEntry
{
    private ScoreEntry()
    {
    }

    public Guid Id { get; private set; }
    public Guid MatchId { get; private set; }

    /// <summary>1-based order of this entry within the match ("Game 1", "Game 2", ...).</summary>
    public int SequenceNumber { get; private set; }

    public int? ScoreA { get; private set; }
    public int? ScoreB { get; private set; }
    public bool ParticipantAWon { get; private set; }

    internal static ScoreEntry Create(Guid matchId, int sequenceNumber, int? scoreA, int? scoreB, bool participantAWon) => new()
    {
        Id = Guid.NewGuid(),
        MatchId = matchId,
        SequenceNumber = sequenceNumber,
        ScoreA = scoreA,
        ScoreB = scoreB,
        ParticipantAWon = participantAWon
    };
}

/// <summary>Domain-only input for a single detailed score entry, supplied by the application layer.</summary>
public readonly record struct ScoreEntryInput(int? ScoreA, int? ScoreB, bool ParticipantAWon);
