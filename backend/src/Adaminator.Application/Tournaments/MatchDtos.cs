using Adaminator.Domain.Enums;

namespace Adaminator.Application.Tournaments;

public record ScoreEntryInputDto(int? ScoreA, int? ScoreB, bool ParticipantAWon);

public record ScoreEntryDto(int SequenceNumber, int? ScoreA, int? ScoreB, bool ParticipantAWon);

public record SaveMatchResultRequest(MatchFormat MatchFormat, ScoreType ScoreType, IReadOnlyList<ScoreEntryInputDto> Entries);

public record CompleteMatchRequest(MatchFormat MatchFormat, ScoreType ScoreType, IReadOnlyList<ScoreEntryInputDto> Entries);

public record ForfeitMatchRequest(Guid WinnerId);
