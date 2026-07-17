using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;
// The MatchFormat/ScoreType properties below shadow their own enum type names, so unqualified
// enum member access (e.g. ScoreType.Points) inside this class's instance methods fails with
// CS0119; these aliases let call sites stay short instead of fully-qualifying every reference.
using DomainMatchFormat = Adaminator.Domain.Enums.MatchFormat;
using DomainScoreType = Adaminator.Domain.Enums.ScoreType;

namespace Adaminator.Domain.Entities;

/// <summary>
/// The primary competitive unit and the source of truth for bracket progression.
/// A slot (<see cref="ParticipantAId"/>/<see cref="ParticipantBId"/>) is null while it is still
/// unresolved (to be filled by an upstream match winner). Result mutation is exposed only as
/// <c>internal</c> members, invoked exclusively through the owning <see cref="Tournament"/>.
/// </summary>
public class Match
{
    private readonly List<ScoreEntry> _scoreEntries = new();

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

    public bool IsDecided => Status is MatchStatus.Completed or MatchStatus.Forfeit;

    /// <summary>The losing participant, once decided; null otherwise.</summary>
    public Guid? LoserId => WinnerId is null ? null : (WinnerId == ParticipantAId ? ParticipantBId : ParticipantAId);

    public ScoreType? ScoreType { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Monotonic, tournament-scoped ordinal assigned when this match is decided; used to find the latest completed match for Undo (FR-UNDO-001).</summary>
    public long? CompletionSequence { get; private set; }

    /// <summary>
    /// Double Elimination only: where this match's winner/loser is routed once decided, resolved
    /// once (after bye-cascade collapse) when the tournament starts and immutable thereafter. Null
    /// for Single Elimination, which computes its single forward route on the fly instead
    /// (<see cref="Brackets.SingleEliminationBracket.NextWinnerSlot"/>) and for the Grand Final's
    /// winner route (there is nowhere further to go).
    /// </summary>
    public Guid? WinnerToMatchId { get; private set; }
    public bool? WinnerToSlotA { get; private set; }

    /// <summary>Double Elimination only: where this match's loser is routed. Null for a Loser Bracket match or the Grand Final (the loser is not routed anywhere further) and for Single Elimination.</summary>
    public Guid? LoserToMatchId { get; private set; }
    public bool? LoserToSlotA { get; private set; }

    public IReadOnlyCollection<ScoreEntry> ScoreEntries => _scoreEntries.AsReadOnly();

    internal static Match Create(
        Guid tournamentId,
        BracketSegment segment,
        int round,
        int indexInRound,
        Guid? participantAId,
        Guid? participantBId,
        MatchFormat matchFormat,
        ScoreType scoreType) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = tournamentId,
        Segment = segment,
        Round = round,
        IndexInRound = indexInRound,
        ParticipantAId = participantAId,
        ParticipantBId = participantBId,
        MatchFormat = matchFormat,
        ScoreType = scoreType,
        Status = MatchStatus.Pending,
        WinnerId = null
    };

    // ---- Result entry (BR-013 through BR-020; called only via Tournament) ----

    /// <summary>Persists a (possibly partial) detailed score. Always leaves the match undecided (FR-MATCH-006/007, AC-SCORE-002).</summary>
    internal void SaveResult(MatchFormat matchFormat, ScoreType scoreType, IReadOnlyList<ScoreEntryInput> entries)
    {
        EnsureNotDecided();
        var built = BuildEntries(matchFormat, scoreType, entries);

        ApplyEntries(matchFormat, scoreType, built);
        WinnerId = null;
        Status = built.Count > 0 ? MatchStatus.InProgress : MatchStatus.Pending;
    }

    /// <summary>Persists the final detailed score and decides the match. Rejects a non-decisive score (AC-SCORE-003).</summary>
    internal void Complete(
        MatchFormat matchFormat,
        ScoreType scoreType,
        IReadOnlyList<ScoreEntryInput> entries,
        long completionSequence,
        DateTimeOffset completedAt)
    {
        EnsureNotDecided();
        var built = BuildEntries(matchFormat, scoreType, entries);

        var required = matchFormat.RequiredWins();
        var winsA = built.Count(e => e.ParticipantAWon);
        var winsB = built.Count - winsA;

        Guid winnerId;
        if (winsA >= required)
        {
            winnerId = ParticipantAId!.Value;
        }
        else if (winsB >= required)
        {
            winnerId = ParticipantBId!.Value;
        }
        else
        {
            throw new DomainException("Neither participant has reached the required number of wins yet.");
        }

        ApplyEntries(matchFormat, scoreType, built);
        WinnerId = winnerId;
        Status = MatchStatus.Completed;
        CompletedAt = completedAt;
        CompletionSequence = completionSequence;
    }

    private void ApplyEntries(MatchFormat matchFormat, ScoreType scoreType, List<ScoreEntry> built)
    {
        MatchFormat = matchFormat;
        ScoreType = scoreType;
        _scoreEntries.Clear();
        _scoreEntries.AddRange(built);
    }

    /// <summary>Completes the match by forfeit. Detailed scores are not required and any previously saved partial score is left untouched (BR-020, FR-FORFEIT-001..004).</summary>
    internal void CompleteAsForfeit(Guid winnerId, long completionSequence, DateTimeOffset completedAt)
    {
        EnsureNotDecided();
        if (winnerId != ParticipantAId && winnerId != ParticipantBId)
        {
            throw new DomainException("The forfeit winner must be one of the two participants.");
        }

        WinnerId = winnerId;
        Status = MatchStatus.Forfeit;
        CompletedAt = completedAt;
        CompletionSequence = completionSequence;
    }

    /// <summary>
    /// Double Elimination only: records the resolved (post bye-cascade) forward routes for this
    /// match. Called exactly once per match, by <see cref="Brackets.DoubleEliminationBracket"/>
    /// while building the graph at tournament start; never recomputed afterward.
    /// </summary>
    internal void SetRoutes(Guid? winnerToMatchId, bool? winnerToSlotA, Guid? loserToMatchId, bool? loserToSlotA)
    {
        WinnerToMatchId = winnerToMatchId;
        WinnerToSlotA = winnerToSlotA;
        LoserToMatchId = loserToMatchId;
        LoserToSlotA = loserToSlotA;
    }

    /// <summary>Fills an empty downstream slot with an advancing participant (BR-021).</summary>
    internal void ResolveSlot(bool slotA, Guid participantId)
    {
        if (slotA)
        {
            if (ParticipantAId is not null)
            {
                throw new DomainException("Participant A slot is already filled.");
            }

            ParticipantAId = participantId;
        }
        else
        {
            if (ParticipantBId is not null)
            {
                throw new DomainException("Participant B slot is already filled.");
            }

            ParticipantBId = participantId;
        }
    }

    /// <summary>Reverts a slot filled by advancement, used by Undo (FR-UNDO-003).</summary>
    internal void ClearSlot(bool slotA, Guid expectedParticipantId)
    {
        if (slotA)
        {
            if (ParticipantAId != expectedParticipantId)
            {
                throw new DomainException("Participant A slot does not hold the expected participant.");
            }

            ParticipantAId = null;
        }
        else
        {
            if (ParticipantBId != expectedParticipantId)
            {
                throw new DomainException("Participant B slot does not hold the expected participant.");
            }

            ParticipantBId = null;
        }
    }

    /// <summary>Reverts a Completed/Forfeit match back to its pre-decision state. Detailed scores are preserved (FR-UNDO-004/005).</summary>
    internal void Undo()
    {
        if (Status is not (MatchStatus.Completed or MatchStatus.Forfeit))
        {
            throw new DomainException("Only a completed or forfeited match can be undone.");
        }

        WinnerId = null;
        CompletedAt = null;
        CompletionSequence = null;
        Status = _scoreEntries.Count > 0 ? MatchStatus.InProgress : MatchStatus.Pending;
    }

    private void EnsureNotDecided()
    {
        if (Status is MatchStatus.Completed or MatchStatus.Forfeit)
        {
            throw new DomainException("This match has already been decided and can no longer be edited.");
        }

        if (ParticipantAId is null || ParticipantBId is null)
        {
            throw new DomainException("A match needs both participants before results can be recorded.");
        }
    }

    private List<ScoreEntry> BuildEntries(MatchFormat matchFormat, ScoreType scoreType, IReadOnlyList<ScoreEntryInput> entries)
    {
        var maxGames = matchFormat.MaxGames();
        if (entries.Count > maxGames)
        {
            throw new DomainException($"A {matchFormat} match cannot have more than {maxGames} game(s)/set(s).");
        }

        if (scoreType == DomainScoreType.WinnerOnly && matchFormat != DomainMatchFormat.Bo1)
        {
            throw new DomainException("Winner Only scoring is valid only for BO1 matches.");
        }

        var built = new List<ScoreEntry>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (scoreType == DomainScoreType.Points && (entry.ScoreA is null || entry.ScoreB is null))
            {
                throw new DomainException("Points scoring requires a numeric score for both participants in every entry.");
            }

            if (entry.ScoreA is not null && entry.ScoreB is not null)
            {
                if (entry.ScoreA == entry.ScoreB)
                {
                    throw new DomainException("A game or set may not end in a draw.");
                }

                if (entry.ParticipantAWon != entry.ScoreA > entry.ScoreB)
                {
                    throw new DomainException("The declared winner does not match the entered scores.");
                }
            }

            built.Add(ScoreEntry.Create(Id, i + 1, entry.ScoreA, entry.ScoreB, entry.ParticipantAWon));
        }

        return built;
    }
}
