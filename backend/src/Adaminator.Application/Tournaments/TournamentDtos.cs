using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

/// <summary>
/// The settings a create and an edit carry identically. Both request records satisfy it through their
/// positional properties, so their validation is one rule set rather than two copies that can drift.
/// </summary>
public interface ITournamentSettings
{
    string Name { get; }
    string? Notes { get; }
    TournamentType Type { get; }
    MatchFormat DefaultMatchFormat { get; }
    ScoreType DefaultScoreType { get; }
    bool ThirdPlaceEnabled { get; }
    int GroupCount { get; }
    TiebreakerPolicy TiebreakerPolicy { get; }
    MatchFormat? GroupStageMatchFormat { get; }
    MatchFormat? UpperBracketFormat { get; }
    MatchFormat? LowerBracketFormat { get; }
    MatchFormat? GrandFinalFormat { get; }
    GroupStageKind GroupStageKind { get; }
    PlayoffKind PlayoffKind { get; }
    int PlayoffSize { get; }
    int SwissRounds { get; }
}

/// <summary>Payload for creating a tournament (Flow 1).</summary>
public record CreateTournamentRequest(
    string Name,
    DateOnly Date,
    string? Notes,
    TournamentType Type,
    MatchFormat DefaultMatchFormat,
    bool ThirdPlaceEnabled,
    ScoreType DefaultScoreType = ScoreType.Games,
    int GroupCount = 0,
    TiebreakerPolicy TiebreakerPolicy = TiebreakerPolicy.ComputedThenMatch,
    MatchFormat? GroupStageMatchFormat = null,
    MatchFormat? UpperBracketFormat = null,
    MatchFormat? LowerBracketFormat = null,
    MatchFormat? GrandFinalFormat = null,
    GroupStageKind GroupStageKind = GroupStageKind.RoundRobin,
    PlayoffKind PlayoffKind = PlayoffKind.DoubleElimination,
    int PlayoffSize = 0,
    int SwissRounds = 0) : ITournamentSettings;

/// <summary>Payload for editing a Planned tournament (FR-TOUR-002).</summary>
public record UpdateTournamentRequest(
    string Name,
    DateOnly Date,
    string? Notes,
    TournamentType Type,
    MatchFormat DefaultMatchFormat,
    bool ThirdPlaceEnabled,
    ScoreType DefaultScoreType = ScoreType.Games,
    int GroupCount = 0,
    TiebreakerPolicy TiebreakerPolicy = TiebreakerPolicy.ComputedThenMatch,
    MatchFormat? GroupStageMatchFormat = null,
    MatchFormat? UpperBracketFormat = null,
    MatchFormat? LowerBracketFormat = null,
    MatchFormat? GrandFinalFormat = null,
    GroupStageKind GroupStageKind = GroupStageKind.RoundRobin,
    PlayoffKind PlayoffKind = PlayoffKind.DoubleElimination,
    int PlayoffSize = 0,
    int SwissRounds = 0) : ITournamentSettings;

/// <summary>Full admin-facing representation of a tournament.</summary>
public record TournamentDto(
    Guid Id,
    string Name,
    DateOnly Date,
    string? Notes,
    TournamentType Type,
    MatchFormat DefaultMatchFormat,
    bool ThirdPlaceEnabled,
    ScoreType DefaultScoreType,
    int GroupCount,
    TiebreakerPolicy TiebreakerPolicy,
    MatchFormat GroupStageMatchFormat,
    MatchFormat UpperBracketFormat,
    MatchFormat LowerBracketFormat,
    MatchFormat GrandFinalFormat,
    GroupStageKind GroupStageKind,
    PlayoffKind PlayoffKind,
    /// <summary>The admin's raw choice; 0 means "the largest capacity the roster fills".</summary>
    int PlayoffSize,
    /// <summary>The admin's raw choice; 0 means "ceil(log2 roster)".</summary>
    int SwissRounds,
    /// <summary>The playoff cut actually in force, with 0 resolved against the current roster.</summary>
    int PlayoffCapacity,
    /// <summary>The Swiss round count actually in force, with 0 resolved against the current roster.</summary>
    int ResolvedSwissRounds,
    TournamentStatus Status,
    string PublicToken,
    DateTimeOffset CreatedAt);

/// <summary>Condensed representation used for the dashboard cards (UI/UX guidelines).</summary>
public record TournamentSummaryDto(
    Guid Id,
    string Name,
    DateOnly Date,
    TournamentType Type,
    TournamentStatus Status,
    int ParticipantCount);

/// <summary>Read-only representation exposed on the public tournament page (FR-PUBLIC-002).</summary>
public record PublicTournamentDto(
    string Name,
    DateOnly Date,
    string? Notes,
    TournamentType Type,
    MatchFormat DefaultMatchFormat,
    ScoreType DefaultScoreType,
    int GroupCount,
    TiebreakerPolicy TiebreakerPolicy,
    MatchFormat GroupStageMatchFormat,
    MatchFormat UpperBracketFormat,
    MatchFormat LowerBracketFormat,
    MatchFormat GrandFinalFormat,
    GroupStageKind GroupStageKind,
    PlayoffKind PlayoffKind,
    int PlayoffCapacity,
    int ResolvedSwissRounds,
    TournamentStatus Status,
    IReadOnlyList<ParticipantDto> Participants,
    BracketDto? Bracket);
