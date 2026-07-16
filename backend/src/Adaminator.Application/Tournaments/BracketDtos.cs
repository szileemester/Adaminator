using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

public record BracketSlotDto(Guid ParticipantId, string Name);

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

public record BracketDto(
    TournamentType Type,
    TournamentStatus Status,
    IReadOnlyList<BracketRoundDto> WinnerRounds,
    IReadOnlyList<BracketRoundDto> LoserRounds,
    BracketMatchDto? GrandFinal,
    BracketMatchDto? ThirdPlace,
    BracketSlotDto? ThirdPlacePodium);
