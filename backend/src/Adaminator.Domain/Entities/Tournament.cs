using Adaminator.Domain.Brackets;
using Adaminator.Domain.Enums;
using Adaminator.Domain.Exceptions;

namespace Adaminator.Domain.Entities;

/// <summary>
/// A single competitive event managed in Adaminator and the aggregate root for its participants
/// and matches. Matches are the source of truth; the bracket is a projection of them.
/// </summary>
public class Tournament
{
    public const int NameMaxLength = 200;
    public const int NotesMaxLength = 2000;
    public const int MinParticipants = 2;
    public const int MaxParticipants = 32;

    private readonly List<Participant> _participants = new();
    private readonly List<Match> _matches = new();

    // Required by EF Core; not used directly by application code.
    private Tournament()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateOnly Date { get; private set; }
    public string? Notes { get; private set; }
    public TournamentType Type { get; private set; }
    public MatchFormat DefaultMatchFormat { get; private set; }

    /// <summary>Third Place Match is only ever enabled for Single Elimination (BR-006, FR-TOUR-007/008).</summary>
    public bool ThirdPlaceEnabled { get; private set; }

    public TournamentStatus Status { get; private set; }

    /// <summary>
    /// Opaque, non-sequential identifier used for the public read-only view so that
    /// internal database identity is not exposed (NFR security guidance).
    /// </summary>
    public string PublicToken { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Participant> Participants => _participants.AsReadOnly();
    public IReadOnlyCollection<Match> Matches => _matches.AsReadOnly();

    /// <summary>True once a bracket has been generated (all participants have a seed).</summary>
    public bool IsSeeded => _participants.Count >= MinParticipants && _participants.All(p => p.Seed > 0);

    public static Tournament Create(
        string name,
        DateOnly date,
        string? notes,
        TournamentType type,
        MatchFormat defaultMatchFormat,
        bool thirdPlaceEnabled,
        DateTimeOffset createdAt)
    {
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Status = TournamentStatus.Planned,
            PublicToken = GenerateToken(),
            CreatedAt = createdAt
        };

        tournament.SetDetails(name, date, notes, type, defaultMatchFormat, thirdPlaceEnabled);
        return tournament;
    }

    /// <summary>Updates editable settings. Allowed only while Planned (FR-TOUR-002, BR-002).</summary>
    public void UpdateDetails(
        string name,
        DateOnly date,
        string? notes,
        TournamentType type,
        MatchFormat defaultMatchFormat,
        bool thirdPlaceEnabled)
    {
        EnsurePlanned("edited");
        SetDetails(name, date, notes, type, defaultMatchFormat, thirdPlaceEnabled);
    }

    // ---- Participant management (Planned only) ----

    public Participant AddParticipant(string name)
    {
        EnsurePlanned("changed");
        if (_participants.Count >= MaxParticipants)
        {
            throw new DomainException($"A tournament may have at most {MaxParticipants} participants.");
        }

        var trimmed = (name ?? string.Empty).Trim();
        EnsureUniqueName(trimmed, excludeId: null);

        var participant = Participant.Create(Id, trimmed);
        _participants.Add(participant);
        ResetSeeding();
        return participant;
    }

    public void RenameParticipant(Guid participantId, string name)
    {
        EnsurePlanned("changed");
        var participant = FindParticipant(participantId);
        var trimmed = (name ?? string.Empty).Trim();
        EnsureUniqueName(trimmed, excludeId: participantId);
        participant.Rename(trimmed);
    }

    public void RemoveParticipant(Guid participantId)
    {
        EnsurePlanned("changed");
        var participant = FindParticipant(participantId);
        _participants.Remove(participant);
        ResetSeeding();
    }

    // ---- Bracket preview (Planned only) ----

    /// <summary>
    /// Applies a seed ordering and bye selection to the current roster. Used both by random
    /// generation and by manual preview edits. Requires exactly the number of byes the bracket size
    /// demands, and every participant to appear exactly once.
    /// </summary>
    public void ApplySeeding(IReadOnlyList<Guid> orderedParticipantIds, IReadOnlyCollection<Guid> byeParticipantIds)
    {
        EnsurePlanned("changed");
        if (_participants.Count < MinParticipants)
        {
            throw new DomainException($"At least {MinParticipants} participants are required to generate a bracket.");
        }

        var rosterIds = _participants.Select(p => p.Id).ToHashSet();

        if (orderedParticipantIds.Count != rosterIds.Count ||
            orderedParticipantIds.Distinct().Count() != rosterIds.Count ||
            !orderedParticipantIds.All(rosterIds.Contains))
        {
            throw new DomainException("The seed order must include each participant exactly once.");
        }

        var byeSet = byeParticipantIds.ToHashSet();
        if (byeSet.Count != byeParticipantIds.Count || !byeSet.All(rosterIds.Contains))
        {
            throw new DomainException("Bye selection is invalid.");
        }

        var requiredByes = Type == TournamentType.DoubleElimination
            ? DoubleEliminationBracket.ComputeRequiredByes(_participants.Count)
            : SingleEliminationBracket.ComputeRequiredByes(_participants.Count);
        if (byeSet.Count != requiredByes)
        {
            throw new DomainException($"Exactly {requiredByes} bye(s) must be selected; {byeSet.Count} chosen.");
        }

        for (var i = 0; i < orderedParticipantIds.Count; i++)
        {
            var participant = _participants.First(p => p.Id == orderedParticipantIds[i]);
            participant.SetSeed(i + 1, byeSet.Contains(participant.Id));
        }
    }

    // ---- Start (Planned -> Running) ----

    public void Start()
    {
        EnsurePlanned("started");
        if (_participants.Count is < MinParticipants or > MaxParticipants)
        {
            throw new DomainException($"A tournament needs between {MinParticipants} and {MaxParticipants} participants to start.");
        }

        if (!IsSeeded)
        {
            throw new DomainException("Generate the bracket before starting the tournament.");
        }

        // BuildMatches re-validates the bye count against the bracket size.
        var matches = Type == TournamentType.DoubleElimination
            ? DoubleEliminationBracket.BuildMatches(this)
            : SingleEliminationBracket.BuildMatches(this);
        _matches.AddRange(matches);
        Status = TournamentStatus.Running;
    }

    // ---- Match results ----

    /// <summary>Saves a (possibly partial) detailed score. Never decides the match (FR-MATCH-006/007).</summary>
    public void SaveMatchResult(Guid matchId, MatchFormat matchFormat, ScoreType scoreType, IReadOnlyList<ScoreEntryInput> entries) =>
        FindMatch(matchId).SaveResult(matchFormat, scoreType, entries);

    /// <summary>Saves the deciding detailed score, sets the winner and advances it (BR-018 through BR-021).</summary>
    public void CompleteMatch(
        Guid matchId,
        MatchFormat matchFormat,
        ScoreType scoreType,
        IReadOnlyList<ScoreEntryInput> entries,
        DateTimeOffset completedAt)
    {
        var match = FindMatch(matchId);
        match.Complete(matchFormat, scoreType, entries, NextCompletionSequence(), completedAt);
        Advance(match);
        MaybeFinish();
    }

    /// <summary>Completes a match by forfeit; the selected winner advances normally (BR-020, FR-FORFEIT-001..004).</summary>
    public void ForfeitMatch(Guid matchId, Guid winnerId, DateTimeOffset completedAt)
    {
        var match = FindMatch(matchId);
        match.CompleteAsForfeit(winnerId, NextCompletionSequence(), completedAt);
        Advance(match);
        MaybeFinish();
    }

    /// <summary>
    /// Double Elimination only: the Loser Bracket Final's decided loser - there is no separate
    /// Third Place match (BR-008). Null if undecided, or if this participant count's bye cascade
    /// collapsed the Loser Bracket Final away entirely (see <see cref="Brackets.DoubleEliminationBracket"/>).
    /// </summary>
    public Guid? ThirdPlaceParticipantId
    {
        get
        {
            if (Type != TournamentType.DoubleElimination)
            {
                return null;
            }

            var finalRound = DoubleEliminationBracket.LoserRoundCount(DoubleEliminationBracket.ComputeBracketSize(_participants.Count));
            var loserFinal = _matches.SingleOrDefault(m => m.Segment == BracketSegment.Loser && m.Round == finalRound);
            if (loserFinal?.WinnerId is not { } winnerId)
            {
                return null;
            }

            return winnerId == loserFinal.ParticipantAId ? loserFinal.ParticipantBId : loserFinal.ParticipantAId;
        }
    }

    /// <summary>
    /// True if <paramref name="matchId"/> is currently eligible for <see cref="UndoMatch"/> (BR-022):
    /// it is the tournament's most recently decided match and nothing it fed into has started yet.
    /// </summary>
    public bool CanUndo(Guid matchId)
    {
        var match = _matches.FirstOrDefault(m => m.Id == matchId);
        if (match is null || !IsLatestDecided(match))
        {
            return false;
        }

        if (Type == TournamentType.DoubleElimination)
        {
            var (winnerRouteMatch, _, loserRouteMatch, _) = FindUndoDependentsDoubleElimination(match);
            return !IsBlockedFrom(winnerRouteMatch, loserRouteMatch);
        }

        var (nextWinnerMatch, _, thirdPlaceMatch) = FindUndoDependents(match);
        return !IsBlockedFrom(nextWinnerMatch, thirdPlaceMatch);
    }

    /// <summary>
    /// Reverts the chronologically latest completed/forfeited match, provided nothing it fed into
    /// has started yet (BR-022, FR-UNDO-001..004).
    /// </summary>
    public void UndoMatch(Guid matchId)
    {
        var match = FindMatch(matchId);
        if (match.Status is not (MatchStatus.Completed or MatchStatus.Forfeit))
        {
            throw new DomainException("Only a completed or forfeited match can be undone.");
        }

        if (!IsLatestDecided(match))
        {
            throw new DomainException("Only the most recently completed match can be undone.");
        }

        var (winnerId, loserId) = WinnerAndLoser(match);

        if (Type == TournamentType.DoubleElimination)
        {
            var (winnerRouteMatch, winnerRouteSlotA, loserRouteMatch, loserRouteSlotA) = FindUndoDependentsDoubleElimination(match);

            if (IsBlockedFrom(winnerRouteMatch, loserRouteMatch))
            {
                throw new DomainException("This match cannot be undone because a dependent match has already started.");
            }

            winnerRouteMatch?.ClearSlot(winnerRouteSlotA, winnerId);
            loserRouteMatch?.ClearSlot(loserRouteSlotA, loserId);
        }
        else
        {
            var (nextWinnerMatch, nextWinnerSlotA, thirdPlaceMatch) = FindUndoDependents(match);

            if (IsBlockedFrom(nextWinnerMatch, thirdPlaceMatch))
            {
                throw new DomainException("This match cannot be undone because a dependent match has already started.");
            }

            nextWinnerMatch?.ClearSlot(nextWinnerSlotA, winnerId);
            thirdPlaceMatch?.ClearSlot(SingleEliminationBracket.ThirdPlaceSlotAFromSemifinalIndex(match.IndexInRound), loserId);
        }

        if (Status == TournamentStatus.Finished)
        {
            Status = TournamentStatus.Running;
        }

        match.Undo();
    }

    private void Advance(Match match)
    {
        if (Type == TournamentType.DoubleElimination)
        {
            AdvanceDoubleElimination(match);
            return;
        }

        if (match.Segment != BracketSegment.Winner)
        {
            return;
        }

        var (winnerId, loserId) = WinnerAndLoser(match);
        var rounds = TotalRounds();

        var next = SingleEliminationBracket.NextWinnerSlot(match.Round, match.IndexInRound, rounds);
        if (next is not null)
        {
            FindWinnerMatch(next.Value.Round, next.Value.IndexInRound).ResolveSlot(next.Value.SlotA, winnerId);
        }

        if (ThirdPlaceEnabled && match.Round == rounds - 1)
        {
            var thirdPlace = _matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);
            thirdPlace?.ResolveSlot(SingleEliminationBracket.ThirdPlaceSlotAFromSemifinalIndex(match.IndexInRound), loserId);
        }
    }

    /// <summary>
    /// Double Elimination: routes are pre-resolved (through any bye-cascade collapse) once at
    /// Start() time and stored directly on the match, so advancing just follows them - no round
    /// math needed, unlike Single Elimination's on-the-fly <see cref="SingleEliminationBracket.NextWinnerSlot"/>.
    /// </summary>
    private void AdvanceDoubleElimination(Match match)
    {
        var (winnerId, loserId) = WinnerAndLoser(match);

        if (match.WinnerToMatchId is { } winnerToMatchId)
        {
            FindMatch(winnerToMatchId).ResolveSlot(match.WinnerToSlotA!.Value, winnerId);
        }

        if (match.LoserToMatchId is { } loserToMatchId)
        {
            FindMatch(loserToMatchId).ResolveSlot(match.LoserToSlotA!.Value, loserId);
        }
    }

    /// <summary>The tournament is Finished once the Final (and, if enabled, Third Place) is decided.</summary>
    private void MaybeFinish()
    {
        if (Type == TournamentType.DoubleElimination)
        {
            var grandFinal = _matches.SingleOrDefault(m => m.Segment == BracketSegment.GrandFinal);
            if (grandFinal is not null && IsDecided(grandFinal))
            {
                Status = TournamentStatus.Finished;
            }

            return;
        }

        var rounds = TotalRounds();
        var final = _matches.SingleOrDefault(m => m.Segment == BracketSegment.Winner && m.Round == rounds && m.IndexInRound == 0);
        if (final is null || !IsDecided(final))
        {
            return;
        }

        var thirdPlace = _matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);
        if (thirdPlace is not null && !IsDecided(thirdPlace))
        {
            return;
        }

        Status = TournamentStatus.Finished;
    }

    private int TotalRounds() => SingleEliminationBracket.RoundCount(SingleEliminationBracket.ComputeBracketSize(_participants.Count));

    private bool IsLatestDecided(Match match)
    {
        if (match.CompletionSequence is null)
        {
            return false;
        }

        var latestSequence = _matches.Where(m => m.CompletionSequence.HasValue).Max(m => m.CompletionSequence);
        return match.CompletionSequence == latestSequence;
    }

    /// <summary>The downstream winner-slot and third-place matches, if any, that <paramref name="match"/> feeds into.</summary>
    private (Match? NextWinnerMatch, bool NextWinnerSlotA, Match? ThirdPlaceMatch) FindUndoDependents(Match match)
    {
        var rounds = TotalRounds();

        Match? nextWinnerMatch = null;
        var nextWinnerSlotA = false;
        if (match.Segment == BracketSegment.Winner)
        {
            var next = SingleEliminationBracket.NextWinnerSlot(match.Round, match.IndexInRound, rounds);
            if (next is not null)
            {
                nextWinnerMatch = FindWinnerMatch(next.Value.Round, next.Value.IndexInRound);
                nextWinnerSlotA = next.Value.SlotA;
            }
        }

        Match? thirdPlaceMatch = null;
        if (ThirdPlaceEnabled && match.Segment == BracketSegment.Winner && match.Round == rounds - 1)
        {
            thirdPlaceMatch = _matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);
        }

        return (nextWinnerMatch, nextWinnerSlotA, thirdPlaceMatch);
    }

    /// <summary>
    /// Double Elimination analog of <see cref="FindUndoDependents"/>: both of a match's own
    /// pre-resolved routes (winner and loser) lead somewhere real, unlike Single Elimination's
    /// single forward route plus an optional separate Third Place route - so this returns both
    /// uniformly rather than forcing DE into that shape.
    /// </summary>
    private (Match? WinnerRouteMatch, bool WinnerRouteSlotA, Match? LoserRouteMatch, bool LoserRouteSlotA) FindUndoDependentsDoubleElimination(Match match)
    {
        var winnerRouteMatch = match.WinnerToMatchId is { } winnerToMatchId ? FindMatch(winnerToMatchId) : null;
        var loserRouteMatch = match.LoserToMatchId is { } loserToMatchId ? FindMatch(loserToMatchId) : null;

        return (winnerRouteMatch, match.WinnerToSlotA ?? false, loserRouteMatch, match.LoserToSlotA ?? false);
    }

    private static (Guid WinnerId, Guid LoserId) WinnerAndLoser(Match match)
    {
        var winnerId = match.WinnerId!.Value;
        var loserId = winnerId == match.ParticipantAId ? match.ParticipantBId!.Value : match.ParticipantAId!.Value;
        return (winnerId, loserId);
    }

    /// <summary>Whether an undo is blocked because either dependent match has already started.</summary>
    private static bool IsBlockedFrom(Match? a, Match? b) =>
        a is { Status: not MatchStatus.Pending } || b is { Status: not MatchStatus.Pending };

    private static bool IsDecided(Match match) => match.Status is MatchStatus.Completed or MatchStatus.Forfeit;

    private long NextCompletionSequence() =>
        _matches.Where(m => m.CompletionSequence.HasValue).Select(m => m.CompletionSequence!.Value).DefaultIfEmpty(0L).Max() + 1;

    private Match FindWinnerMatch(int round, int indexInRound) =>
        _matches.Single(m => m.Segment == BracketSegment.Winner && m.Round == round && m.IndexInRound == indexInRound);

    private Match FindMatch(Guid matchId) =>
        _matches.FirstOrDefault(m => m.Id == matchId)
        ?? throw new DomainException("Match not found in this tournament.");

    private void SetDetails(
        string name,
        DateOnly date,
        string? notes,
        TournamentType type,
        MatchFormat defaultMatchFormat,
        bool thirdPlaceEnabled)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Tournament name is required.");
        }

        if (name.Length > NameMaxLength)
        {
            throw new DomainException($"Tournament name must be at most {NameMaxLength} characters.");
        }

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > NotesMaxLength })
        {
            throw new DomainException($"Tournament notes must be at most {NotesMaxLength} characters.");
        }

        if (type == TournamentType.DoubleElimination && thirdPlaceEnabled)
        {
            throw new DomainException("Third place match is available only for Single Elimination tournaments.");
        }

        Name = name;
        Date = date;
        Notes = notes;
        Type = type;
        DefaultMatchFormat = defaultMatchFormat;
        ThirdPlaceEnabled = type == TournamentType.SingleElimination && thirdPlaceEnabled;
    }

    private void EnsurePlanned(string action)
    {
        if (Status != TournamentStatus.Planned)
        {
            throw new DomainException($"A tournament can only be {action} while it is Planned.");
        }
    }

    private Participant FindParticipant(Guid participantId) =>
        _participants.FirstOrDefault(p => p.Id == participantId)
        ?? throw new DomainException("Participant not found in this tournament.");

    private void EnsureUniqueName(string name, Guid? excludeId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Participant name is required.");
        }

        if (_participants.Any(p => p.Id != excludeId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"A participant named '{name}' already exists in this tournament.");
        }
    }

    private void ResetSeeding()
    {
        foreach (var participant in _participants)
        {
            participant.ClearSeed();
        }
    }

    private static string GenerateToken() => Guid.NewGuid().ToString("N");
}
