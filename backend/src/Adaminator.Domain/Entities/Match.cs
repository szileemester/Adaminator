using Adaminator.Domain.Enums;

namespace Adaminator.Domain.Entities;

/// <summary>
/// The primary competitive unit and the source of truth for bracket progression.
/// A slot (<see cref="ParticipantAId"/>/<see cref="ParticipantBId"/>) is null while it is still
/// unresolved (to be filled by an upstream match winner). Result entry arrives in a later milestone.
/// </summary>
public class Match
{
    private Match()
    {
    }

    public Guid Id { get; private set; }
    public Guid TournamentId { get; private set; }
    public BracketSegment Segment { get; private set; }

    /// <summary>1-based round number within the segment (round 1 is the earliest).</summary>
    public int Round { get; private set; }

    /// <summary>0-based position of the match within its round. May be sparse in round 1 (byes).</summary>
    public int IndexInRound { get; private set; }

    public Guid? ParticipantAId { get; private set; }
    public Guid? ParticipantBId { get; private set; }

    public MatchFormat MatchFormat { get; private set; }
    public MatchStatus Status { get; private set; }
    public Guid? WinnerId { get; private set; }

    internal static Match Create(
        Guid tournamentId,
        BracketSegment segment,
        int round,
        int indexInRound,
        Guid? participantAId,
        Guid? participantBId,
        MatchFormat matchFormat) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = tournamentId,
        Segment = segment,
        Round = round,
        IndexInRound = indexInRound,
        ParticipantAId = participantAId,
        ParticipantBId = participantBId,
        MatchFormat = matchFormat,
        Status = MatchStatus.Pending,
        WinnerId = null
    };
}
