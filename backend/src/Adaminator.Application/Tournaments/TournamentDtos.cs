using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

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
    GroupStageFormat GroupStageFormat = GroupStageFormat.Standard);

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
    GroupStageFormat GroupStageFormat = GroupStageFormat.Standard);

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
    GroupStageFormat GroupStageFormat,
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
    GroupStageFormat GroupStageFormat,
    TournamentStatus Status,
    IReadOnlyList<ParticipantDto> Participants,
    BracketDto? Bracket);
