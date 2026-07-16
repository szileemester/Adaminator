using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

public record ScoreEntryInputDto(int? ScoreA, int? ScoreB, bool ParticipantAWon);

public record ScoreEntryDto(int SequenceNumber, int? ScoreA, int? ScoreB, bool ParticipantAWon);

/// <summary>Shared shape for both the partial-save and completing-score requests; they accept identical payloads.</summary>
public record MatchScoreRequest(MatchFormat MatchFormat, ScoreType ScoreType, IReadOnlyList<ScoreEntryInputDto> Entries);

public record ForfeitMatchRequest(Guid WinnerId);
