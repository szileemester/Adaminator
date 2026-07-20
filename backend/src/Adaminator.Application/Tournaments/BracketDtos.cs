using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

public record BracketSlotDto(Guid ParticipantId, string Name, string? Emoji);

public record BracketMatchDto(
    Guid Id,
    BracketSegment Segment,
    int Round,
    int IndexInRound,
    BracketSlotDto? ParticipantA,
    BracketSlotDto? ParticipantB,
    MatchStatus Status,
    Guid? WinnerId,
    MatchFormat MatchFormat,
    ScoreType? ScoreType,
    IReadOnlyList<ScoreEntryDto> Entries,
    int AggregateScoreA,
    int AggregateScoreB,
    DateTimeOffset? CompletedAt,
    bool CanUndo);

public record BracketRoundDto(int Round, string Title, IReadOnlyList<BracketMatchDto> Matches);

/// <summary>Round Robin only: a participant's ranked won-loss record, sorted wins desc, losses asc, name asc.</summary>
public record StandingRowDto(int Rank, Guid ParticipantId, string Name, string? Emoji, int Played, int Wins, int Losses);

/// <summary>
/// Single/Double Elimination only: one rung of the final-placements leaderboard - "Champion",
/// "Runner-up", "3rd Place", or "Eliminated in {round}" for everyone else. RankStart/RankEnd reflect
/// the group's fixed bracket position (e.g. semifinal losers always occupy 3-4) even before that
/// round is fully decided; RankStart != RankEnd means a tie (e.g. both semifinal losers when there's
/// no Third Place match).
/// </summary>
public record PlacementGroupDto(int RankStart, int RankEnd, string Label, IReadOnlyList<BracketSlotDto> Participants);

/// <summary>Group Stage + Playoff only: one group's round-robin schedule, current standings, and any played tie-breaker matches.</summary>
public record GroupDto(
    int GroupIndex,
    IReadOnlyList<BracketRoundDto> Rounds,
    IReadOnlyList<StandingRowDto> Standings,
    IReadOnlyList<BracketRoundDto> TiebreakerRounds);

public record BracketDto(
    TournamentType Type,
    TournamentStatus Status,
    IReadOnlyList<BracketRoundDto> WinnerRounds,
    IReadOnlyList<BracketRoundDto> LoserRounds,
    BracketMatchDto? GrandFinal,
    BracketMatchDto? ThirdPlace,
    BracketSlotDto? ThirdPlacePodium,
    IReadOnlyList<StandingRowDto> Standings,
    IReadOnlyList<PlacementGroupDto> Placements,
    IReadOnlyList<GroupDto> Groups,
    /// <summary>Round Robin only: played tie-breaker matches; the Group Stage + Playoff carries these per-group on <see cref="GroupDto"/>.</summary>
    IReadOnlyList<BracketRoundDto> TiebreakerRounds,
    bool NeedsTiebreakers,
    bool CanStartPlayoffs,
    bool CanFinish);
