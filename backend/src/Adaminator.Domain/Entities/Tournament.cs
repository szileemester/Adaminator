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
    public ScoreType DefaultScoreType { get; private set; }

    /// <summary>Third Place Match is only ever enabled for Single Elimination (BR-006, FR-TOUR-007/008).</summary>
    public bool ThirdPlaceEnabled { get; private set; }

    /// <summary>Group Stage + Playoff only: how many groups the roster is drawn into. 0 for every other type.</summary>
    public int GroupCount { get; private set; }

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
        ScoreType defaultScoreType,
        bool thirdPlaceEnabled,
        DateTimeOffset createdAt,
        int groupCount = 0)
    {
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Status = TournamentStatus.Planned,
            PublicToken = GenerateToken(),
            CreatedAt = createdAt
        };

        tournament.SetDetails(name, date, notes, type, defaultMatchFormat, defaultScoreType, thirdPlaceEnabled, groupCount);
        return tournament;
    }

    /// <summary>Updates editable settings. Allowed only while Planned (FR-TOUR-002, BR-002).</summary>
    public void UpdateDetails(
        string name,
        DateOnly date,
        string? notes,
        TournamentType type,
        MatchFormat defaultMatchFormat,
        ScoreType defaultScoreType,
        bool thirdPlaceEnabled,
        int groupCount = 0)
    {
        EnsurePlanned("edited");
        SetDetails(name, date, notes, type, defaultMatchFormat, defaultScoreType, thirdPlaceEnabled, groupCount);
    }

    // ---- Participant management (Planned only) ----

    public Participant AddParticipant(string name, string? emoji = null)
    {
        EnsurePlanned("changed");
        if (_participants.Count >= MaxParticipants)
        {
            throw new DomainException($"A tournament may have at most {MaxParticipants} participants.");
        }

        var trimmed = (name ?? string.Empty).Trim();
        EnsureUniqueName(trimmed, excludeId: null);

        var participant = Participant.Create(Id, trimmed, emoji);
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

    /// <summary>
    /// Chooses a participant's emoji. Write-once (see <see cref="Participant.SetEmoji"/>) and, like every
    /// other roster edit, only while the tournament is still Planned.
    /// </summary>
    public void SetParticipantEmoji(Guid participantId, string? emoji)
    {
        EnsurePlanned("changed");
        FindParticipant(participantId).SetEmoji(emoji);
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

        var requiredByes = Type switch
        {
            TournamentType.DoubleElimination => DoubleEliminationBracket.ComputeRequiredByes(_participants.Count),
            TournamentType.RoundRobin => RoundRobinBracket.ComputeRequiredByes(_participants.Count),
            _ => SingleEliminationBracket.ComputeRequiredByes(_participants.Count)
        };
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

    /// <summary>
    /// Group Stage + Playoff only: randomly, balanced, deals the roster into <see cref="GroupCount"/>
    /// groups (regenerate-able while Planned). Each participant gets a within-group order that drives
    /// its group's round-robin schedule.
    /// </summary>
    public void DrawGroups()
    {
        EnsurePlanned("changed");
        if (Type != TournamentType.GroupStagePlayoff)
        {
            throw new DomainException("Group draw is only available for Group Stage + Playoff tournaments.");
        }

        GroupStagePlayoffBracket.ValidateShape(_participants.Count, GroupCount);

        var shuffled = _participants.OrderBy(_ => Random.Shared.Next()).ToList();
        var perGroup = _participants.Count / GroupCount;
        for (var i = 0; i < shuffled.Count; i++)
        {
            shuffled[i].SetGroup(groupIndex: i / perGroup, seedWithinGroup: i % perGroup + 1);
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

        if (Type == TournamentType.GroupStagePlayoff)
        {
            if (_participants.Any(p => p.GroupIndex is null))
            {
                throw new DomainException("Draw the groups before starting the tournament.");
            }

            GroupStagePlayoffBracket.ValidateShape(_participants.Count, GroupCount);
        }
        else if (!IsSeeded)
        {
            throw new DomainException("Generate the bracket before starting the tournament.");
        }

        // BuildMatches re-validates the bye count against the bracket size. Group Stage + Playoff
        // starts with only the group stage; the playoff is built later by StartPlayoffs().
        var matches = Type switch
        {
            TournamentType.DoubleElimination => DoubleEliminationBracket.BuildMatches(this),
            TournamentType.RoundRobin => RoundRobinBracket.BuildMatches(this),
            TournamentType.GroupStagePlayoff => GroupStagePlayoffBracket.BuildGroupStage(this),
            _ => SingleEliminationBracket.BuildMatches(this)
        };
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
    }

    /// <summary>Completes a match by forfeit; the selected winner advances normally (BR-020, FR-FORFEIT-001..004).</summary>
    public void ForfeitMatch(Guid matchId, Guid winnerId, DateTimeOffset completedAt)
    {
        var match = FindMatch(matchId);
        match.CompleteAsForfeit(winnerId, NextCompletionSequence(), completedAt);
        Advance(match);
    }

    /// <summary>True once every deciding match is decided and the admin can finish the tournament by hand.</summary>
    public bool CanFinish => Status == TournamentStatus.Running && IsReadyToFinish();

    /// <summary>
    /// Manually transitions a Running tournament to Finished. Finishing is a deliberate admin action,
    /// not automatic - completing the last match only makes <see cref="CanFinish"/> true.
    /// </summary>
    public void Finish()
    {
        if (Status != TournamentStatus.Running)
        {
            throw new DomainException("Only a Running tournament can be finished.");
        }

        if (!IsReadyToFinish())
        {
            throw new DomainException("The tournament cannot be finished until all matches are decided.");
        }

        Status = TournamentStatus.Finished;
    }

    /// <summary>True once the group stage is complete and the admin can generate the playoff by hand (Group Stage + Playoff only).</summary>
    public bool CanStartPlayoffs =>
        Type == TournamentType.GroupStagePlayoff
        && Status == TournamentStatus.Running
        && !PlayoffStarted
        && GroupStageDecided;

    /// <summary>
    /// Group Stage + Playoff only: seeds and builds the double-elimination playoff from the final
    /// group standings (each group's top half into the Winner Bracket, bottom half into the Loser
    /// Bracket). A deliberate admin action, gated on <see cref="CanStartPlayoffs"/>.
    /// </summary>
    public void StartPlayoffs()
    {
        if (Type != TournamentType.GroupStagePlayoff)
        {
            throw new DomainException("Playoffs are only available for Group Stage + Playoff tournaments.");
        }

        if (Status != TournamentStatus.Running)
        {
            throw new DomainException("Playoffs can only be started while the tournament is Running.");
        }

        if (PlayoffStarted)
        {
            throw new DomainException("The playoff has already started.");
        }

        if (!GroupStageDecided)
        {
            throw new DomainException("Every group match must be decided before starting the playoff.");
        }

        var roster = _participants.ToDictionary(p => p.Id);
        var groupStandings = new List<IReadOnlyList<Guid>>(GroupCount);
        for (var g = 0; g < GroupCount; g++)
        {
            var groupParticipants = _participants.Where(p => p.GroupIndex == g).ToList();
            var groupMatches = GroupMatches.Where(m => m.GroupIndex == g);
            var ranked = RoundRobinStandings.Rank(groupMatches, groupParticipants, roster).Select(r => r.ParticipantId).ToList();
            groupStandings.Add(ranked);
        }

        var (upperSeeds, lowerSeeds) = GroupStagePlayoffBracket.SeedPools(groupStandings);
        _matches.AddRange(GroupStagePlayoffBracket.BuildPlayoff(this, upperSeeds, lowerSeeds));
    }

    private IEnumerable<Match> GroupMatches => _matches.Where(m => m.Segment == BracketSegment.RoundRobin);

    /// <summary>Whether the group stage exists and every one of its matches is decided (single pass over the match list).</summary>
    private bool GroupStageDecided
    {
        get
        {
            var any = false;
            foreach (var match in _matches)
            {
                if (match.Segment != BracketSegment.RoundRobin)
                {
                    continue;
                }

                if (!IsDecided(match))
                {
                    return false;
                }

                any = true;
            }

            return any;
        }
    }

    /// <summary>Group Stage + Playoff: whether the playoff bracket has been generated (any elimination-segment match exists).</summary>
    private bool PlayoffStarted =>
        _matches.Any(m => m.Segment is BracketSegment.Winner or BracketSegment.Loser or BracketSegment.GrandFinal);

    /// <summary>
    /// Whether this tournament's elimination matches advance/undo by following routes persisted on the
    /// match (Double Elimination, and the Group Stage + Playoff playoff) rather than Single
    /// Elimination's positional round math. Callers check this only after excluding
    /// <see cref="BracketSegment.RoundRobin"/> matches, which never advance at all.
    /// </summary>
    private bool UsesStoredRoutes => Type is TournamentType.DoubleElimination or TournamentType.GroupStagePlayoff;

    /// <summary>
    /// Double Elimination only: the Loser Bracket Final's decided loser - there is no separate
    /// Third Place match (BR-008). Null if undecided, or if this participant count's bye cascade
    /// collapsed the Loser Bracket Final away entirely (see <see cref="Brackets.DoubleEliminationBracket"/>).
    /// </summary>
    public Guid? ThirdPlaceParticipantId
    {
        get
        {
            if (Type is not (TournamentType.DoubleElimination or TournamentType.GroupStagePlayoff))
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

        if (match.Segment == BracketSegment.RoundRobin)
        {
            // Round Robin / group-stage matches have no dependents; the latest-decided check suffices.
            return true;
        }

        if (UsesStoredRoutes)
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

        // Round Robin / group-stage matches feed nothing, so there is no dependent slot to clear.
        if (match.Segment != BracketSegment.RoundRobin && UsesStoredRoutes)
        {
            var (winnerRouteMatch, winnerRouteSlotA, loserRouteMatch, loserRouteSlotA) = FindUndoDependentsDoubleElimination(match);

            if (IsBlockedFrom(winnerRouteMatch, loserRouteMatch))
            {
                throw new DomainException("This match cannot be undone because a dependent match has already started.");
            }

            winnerRouteMatch?.ClearSlot(winnerRouteSlotA, winnerId);
            loserRouteMatch?.ClearSlot(loserRouteSlotA, loserId);
        }
        else if (match.Segment != BracketSegment.RoundRobin)
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
        if (match.Segment == BracketSegment.RoundRobin)
        {
            // Round Robin / group-stage matches never feed into another match.
            return;
        }

        if (UsesStoredRoutes)
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

    /// <summary>Whether the Final (and, if enabled, Third Place) - or the Grand Final, or every match for Round Robin - is decided.</summary>
    private bool IsReadyToFinish()
    {
        // Double Elimination and the Group Stage + Playoff playoff both finish on their Grand Final;
        // for the latter that match only exists once the playoff has been started.
        if (Type is TournamentType.DoubleElimination or TournamentType.GroupStagePlayoff)
        {
            var grandFinal = _matches.SingleOrDefault(m => m.Segment == BracketSegment.GrandFinal);
            return grandFinal is not null && IsDecided(grandFinal);
        }

        if (Type == TournamentType.RoundRobin)
        {
            return _matches.All(IsDecided);
        }

        var rounds = TotalRounds();
        var final = _matches.SingleOrDefault(m => m.Segment == BracketSegment.Winner && m.Round == rounds && m.IndexInRound == 0);
        if (final is null || !IsDecided(final))
        {
            return false;
        }

        var thirdPlace = _matches.FirstOrDefault(m => m.Segment == BracketSegment.ThirdPlace);
        return thirdPlace is null || IsDecided(thirdPlace);
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

    private static (Guid WinnerId, Guid LoserId) WinnerAndLoser(Match match) => (match.WinnerId!.Value, match.LoserId!.Value);

    /// <summary>Whether an undo is blocked because either dependent match has already started.</summary>
    private static bool IsBlockedFrom(Match? a, Match? b) =>
        a is { Status: not MatchStatus.Pending } || b is { Status: not MatchStatus.Pending };

    private static bool IsDecided(Match match) => match.IsDecided;

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
        ScoreType defaultScoreType,
        bool thirdPlaceEnabled,
        int groupCount)
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

        if (type != TournamentType.SingleElimination && thirdPlaceEnabled)
        {
            throw new DomainException("Third place match is available only for Single Elimination tournaments.");
        }

        if (defaultScoreType == ScoreType.WinnerOnly && defaultMatchFormat != MatchFormat.Bo1)
        {
            throw new DomainException("Winner Only scoring is valid only for BO1 matches.");
        }

        if (type == TournamentType.GroupStagePlayoff && groupCount < 2)
        {
            throw new DomainException("Group Stage + Playoff needs at least 2 groups.");
        }

        Name = name;
        Date = date;
        Notes = notes;
        Type = type;
        DefaultMatchFormat = defaultMatchFormat;
        DefaultScoreType = defaultScoreType;
        ThirdPlaceEnabled = type == TournamentType.SingleElimination && thirdPlaceEnabled;
        GroupCount = type == TournamentType.GroupStagePlayoff ? groupCount : 0;
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
