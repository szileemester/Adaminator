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

    /// <summary>How standings ties that change an outcome are resolved. Meaningful only for Round Robin and Group Stage + Playoff.</summary>
    public TiebreakerPolicy TiebreakerPolicy { get; private set; }

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
        int groupCount = 0,
        TiebreakerPolicy tiebreakerPolicy = TiebreakerPolicy.ComputedThenMatch)
    {
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Status = TournamentStatus.Planned,
            PublicToken = GenerateToken(),
            CreatedAt = createdAt
        };

        tournament.SetDetails(name, date, notes, type, defaultMatchFormat, defaultScoreType, thirdPlaceEnabled, groupCount, tiebreakerPolicy);
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
        int groupCount = 0,
        TiebreakerPolicy tiebreakerPolicy = TiebreakerPolicy.ComputedThenMatch)
    {
        EnsurePlanned("edited");
        SetDetails(name, date, notes, type, defaultMatchFormat, defaultScoreType, thirdPlaceEnabled, groupCount, tiebreakerPolicy);
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

        // Group sizes need not be equal - a remainder simply makes the earlier groups one bigger.
        var shuffled = _participants.OrderBy(_ => Random.Shared.Next()).ToList();
        var sizes = GroupStagePlayoffBracket.GroupSizes(_participants.Count, GroupCount);
        var next = 0;
        for (var g = 0; g < GroupCount; g++)
        {
            for (var seat = 0; seat < sizes[g]; seat++)
            {
                shuffled[next++].SetGroup(groupIndex: g, seedWithinGroup: seat + 1);
            }
        }
    }

    /// <summary>Group Stage + Playoff: how many participants each group actually holds (sizes can differ by one).</summary>
    private IReadOnlyList<int> CurrentGroupSizes() =>
        Enumerable.Range(0, GroupCount).Select(g => _participants.Count(p => p.GroupIndex == g)).ToList();

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

    /// <summary>True once the group stage is complete (and any tie-breakers resolved) and the admin can generate the playoff by hand (Group Stage + Playoff only).</summary>
    public bool CanStartPlayoffs =>
        Type == TournamentType.GroupStagePlayoff
        && Status == TournamentStatus.Running
        && !PlayoffStarted
        && GroupStageDecided
        && !NeedsTiebreakers
        && TiebreakerMatches.All(IsDecided);

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

        if (NeedsTiebreakers)
        {
            throw new DomainException("Resolve the tie-breakers before starting the playoff.");
        }

        if (!TiebreakerMatches.All(IsDecided))
        {
            throw new DomainException("Every tie-breaker match must be decided before starting the playoff.");
        }

        var capacity = GroupStagePlayoffBracket.PlayoffCapacity(_participants.Count);
        var (upperSeeds, lowerSeeds, _) = GroupStagePlayoffBracket.SeedPools(BuildSeedOrder(capacity), capacity);
        _matches.AddRange(GroupStagePlayoffBracket.BuildPlayoff(this, upperSeeds, lowerSeeds));
    }

    /// <summary>Each group's final standings, best to worst.</summary>
    private List<IReadOnlyList<Guid>> GroupStandings()
    {
        var roster = _participants.ToDictionary(p => p.Id);
        var standings = new List<IReadOnlyList<Guid>>(GroupCount);
        for (var g = 0; g < GroupCount; g++)
        {
            var groupParticipants = _participants.Where(p => p.GroupIndex == g).ToList();
            standings.Add(RoundRobinStandings.Rank(ScopeMatches(g), groupParticipants, roster).Select(r => r.ParticipantId).ToList());
        }

        return standings;
    }

    /// <summary>
    /// The full seeding order: every group winner, then every runner-up, and so on. A level whose
    /// members are competing for fewer slots than there are of them is ordered by the cross-group
    /// decider they played; everything else keeps its group order.
    /// </summary>
    private List<Guid> BuildSeedOrder(int capacity)
    {
        var standings = GroupStandings();
        var levels = GroupStagePlayoffBracket.PlanLevels(CurrentGroupSizes(), capacity);

        var order = new List<Guid>(_participants.Count);
        foreach (var level in levels)
        {
            var members = GroupStagePlayoffBracket.LevelMembers(standings, level.Position);
            order.AddRange(level.Outcome == LevelOutcome.Contested ? OrderByCrossGroupDecider(members) : members);
        }

        return order;
    }

    /// <summary>
    /// Contested levels whose members the cross-group deciders have not separated at the slot they are
    /// competing for. Only the boundary inside the level matters: members tied above or below it are
    /// interchangeable, so they are left alone.
    /// </summary>
    private List<IReadOnlyList<Guid>> UnresolvedContestedLevels(IReadOnlyList<GroupStagePlayoffBracket.PlacementLevel> levels, int capacity)
    {
        var standings = GroupStandings();
        var unresolved = new List<IReadOnlyList<Guid>>();

        foreach (var level in levels.Where(l => l.Outcome == LevelOutcome.Contested))
        {
            var members = GroupStagePlayoffBracket.LevelMembers(standings, level.Position);
            if (members.Count < 2)
            {
                continue;
            }

            // Cut positions expressed relative to this level's own span.
            var localCuts = new[] { capacity / 2, capacity }
                .Where(cut => GroupStagePlayoffBracket.SpansCut(level.Start, level.End, cut))
                .Select(cut => cut - level.Start)
                .ToList();

            // The deciders played so far split the level into runs of equal record; a run still spanning
            // one of those cuts is still contesting that slot.
            var offset = 0;
            foreach (var run in RoundRobinStandings.SplitByScore(members, CrossGroupWins(members)))
            {
                if (run.Count > 1 && localCuts.Any(cut => GroupStagePlayoffBracket.SpansCut(offset, offset + run.Count - 1, cut)))
                {
                    unresolved.Add(run);
                }

                offset += run.Count;
            }
        }

        return unresolved;
    }

    /// <summary>Orders a contested level by its cross-group tie-breaker record, falling back to name so the order is always total.</summary>
    private List<Guid> OrderByCrossGroupDecider(List<Guid> members)
    {
        var wins = CrossGroupWins(members);
        var roster = _participants.ToDictionary(p => p.Id);
        return members
            .OrderByDescending(id => wins[id])
            .ThenBy(id => roster[id].Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Wins each member has in the cross-group tie-breaker matches played between two members of the set.</summary>
    private Dictionary<Guid, int> CrossGroupWins(List<Guid> members) =>
        RoundRobinStandings.WinsAmong(members, _matches.Where(m => m.Segment == BracketSegment.Tiebreaker && m.GroupIndex is null));

    /// <summary>
    /// Round Robin and Group Stage + Playoff only: generates played Bo1 tie-breaker matches for every
    /// tie that this tournament's <see cref="TiebreakerPolicy"/> cannot resolve and that changes an
    /// outcome (the Upper/Lower split per group, or a Round Robin podium place). One-shot: mirrors
    /// <see cref="StartPlayoffs"/>, gated on <see cref="NeedsTiebreakers"/>.
    /// </summary>
    public void StartTiebreakers()
    {
        if (Type is not (TournamentType.RoundRobin or TournamentType.GroupStagePlayoff))
        {
            throw new DomainException("Tie-breakers are only available for Round Robin and Group Stage + Playoff tournaments.");
        }

        if (Status != TournamentStatus.Running)
        {
            throw new DomainException("Tie-breakers can only be resolved while the tournament is Running.");
        }

        if (Type == TournamentType.GroupStagePlayoff && PlayoffStarted)
        {
            throw new DomainException("The playoff has already started.");
        }

        if (!GroupStageDecided)
        {
            throw new DomainException("Every round-robin match must be decided before resolving tie-breakers.");
        }

        if (!TiebreakerMatches.All(IsDecided))
        {
            throw new DomainException("Play out the current tie-breaker matches before generating more.");
        }

        var cohorts = UnresolvedTieCohorts();
        if (cohorts.Count == 0)
        {
            throw new DomainException("There are no ties that require a tie-breaker.");
        }

        foreach (var (groupIndex, members) in cohorts)
        {
            // A previous wave may already occupy rounds 1..n for this scope (a tie-breaker round can
            // itself end in a cycle), so continue numbering after it rather than colliding with it.
            var played = _matches.Where(m => m.Segment == BracketSegment.Tiebreaker && m.GroupIndex == groupIndex).ToList();
            var roundOffset = played.Count == 0 ? 0 : played.Max(m => m.Round);

            _matches.AddRange(RoundRobinBracket.Schedule(
                Id, members, MatchFormat.Bo1, DefaultScoreType, groupIndex, BracketSegment.Tiebreaker, roundOffset));
        }
    }

    private IEnumerable<Match> TiebreakerMatches => _matches.Where(m => m.Segment == BracketSegment.Tiebreaker);

    /// <summary>The round-robin and tie-breaker matches for one scope: a single group (Group Stage + Playoff) or the whole field (Round Robin, <paramref name="groupIndex"/> null).</summary>
    private IEnumerable<Match> ScopeMatches(int? groupIndex) =>
        _matches.Where(m =>
            (m.Segment == BracketSegment.RoundRobin || m.Segment == BracketSegment.Tiebreaker)
            && (groupIndex is null || m.GroupIndex == groupIndex));

    /// <summary>
    /// Round Robin and Group Stage + Playoff only: whether a standings tie that changes an outcome is
    /// still unresolved and needs played tie-breaker matches (none have been generated yet).
    /// </summary>
    public bool NeedsTiebreakers
    {
        get
        {
            // A wave that is still being played is not a reason to generate another one.
            if (Status != TournamentStatus.Running || !GroupStageDecided || !TiebreakerMatches.All(IsDecided))
            {
                return false;
            }

            if (Type == TournamentType.GroupStagePlayoff)
            {
                return !PlayoffStarted && UnresolvedTieCohorts().Count > 0;
            }

            return Type == TournamentType.RoundRobin && UnresolvedTieCohorts().Count > 0;
        }
    }

    /// <summary>The tied cohorts (with their group, null for Round Robin) that need played tie-breaker matches under the current policy.</summary>
    private List<(int? GroupIndex, IReadOnlyList<Guid> Members)> UnresolvedTieCohorts()
    {
        var roster = _participants.ToDictionary(p => p.Id);
        var result = new List<(int?, IReadOnlyList<Guid>)>();

        if (Type == TournamentType.GroupStagePlayoff)
        {
            var capacity = GroupStagePlayoffBracket.PlayoffCapacity(_participants.Count);
            var sizes = CurrentGroupSizes();
            var levels = GroupStagePlayoffBracket.PlanLevels(sizes, capacity);

            // A group placement only needs playing off when finishing one place lower would change the
            // participant's fate (Winner Bracket, Loser Bracket, eliminated, or into a contested level).
            for (var g = 0; g < GroupCount; g++)
            {
                var groupParticipants = _participants.Where(p => p.GroupIndex == g).ToList();
                var cuts = GroupStagePlayoffBracket.GroupBoundaryCuts(levels, sizes[g]);
                foreach (var cohort in RoundRobinStandings.FindUnresolvedTieCohorts(ScopeMatches(g), groupParticipants, roster, TiebreakerPolicy, cuts))
                {
                    result.Add((g, cohort));
                }
            }

            // Group placements have to be settled before the cross-group contests can even be identified.
            if (result.Count == 0)
            {
                result.AddRange(UnresolvedContestedLevels(levels, capacity).Select(members => ((int?)null, members)));
            }
        }
        else
        {
            // Round Robin: only podium (top-3) ties are played out.
            var cuts = new[] { 1, 2, 3 };
            foreach (var cohort in RoundRobinStandings.FindUnresolvedTieCohorts(ScopeMatches(null), _participants, roster, TiebreakerPolicy, cuts))
            {
                result.Add((null, cohort));
            }
        }

        return result;
    }

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

    /// <summary>A flat, unrouted match with no dependents - a Round Robin/group-stage match or a tie-breaker match. It never advances anyone and its undo needs only the latest-decided check.</summary>
    private static bool IsFlatSegment(BracketSegment segment) => segment is BracketSegment.RoundRobin or BracketSegment.Tiebreaker;

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

        if (IsFlatSegment(match.Segment))
        {
            // Round Robin / group-stage / tie-breaker matches have no dependents; the latest-decided check suffices.
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

        // Round Robin / group-stage / tie-breaker matches feed nothing, so there is no dependent slot to clear.
        if (!IsFlatSegment(match.Segment) && UsesStoredRoutes)
        {
            var (winnerRouteMatch, winnerRouteSlotA, loserRouteMatch, loserRouteSlotA) = FindUndoDependentsDoubleElimination(match);

            if (IsBlockedFrom(winnerRouteMatch, loserRouteMatch))
            {
                throw new DomainException("This match cannot be undone because a dependent match has already started.");
            }

            winnerRouteMatch?.ClearSlot(winnerRouteSlotA, winnerId);
            loserRouteMatch?.ClearSlot(loserRouteSlotA, loserId);
        }
        else if (!IsFlatSegment(match.Segment))
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
        if (IsFlatSegment(match.Segment))
        {
            // Round Robin / group-stage / tie-breaker matches never feed into another match.
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
            return _matches.All(IsDecided) && !NeedsTiebreakers;
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
        int groupCount,
        TiebreakerPolicy tiebreakerPolicy)
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
        TiebreakerPolicy = tiebreakerPolicy;
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
