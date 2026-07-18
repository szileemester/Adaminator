namespace Adaminator.Application.Tournaments;

public record ParticipantDto(Guid Id, string Name, string? Emoji, int Seed, bool HasBye, int? GroupIndex);

public record AddParticipantRequest(string Name, string? Emoji = null);

/// <summary>Carries the emoji alongside the name; re-sending the stored emoji is a no-op, changing it is rejected (it is write-once).</summary>
public record UpdateParticipantRequest(string Name, string? Emoji = null);

/// <summary>Editable preview state: the full seed order and the selected bye recipients.</summary>
public record UpdateBracketRequest(IReadOnlyList<Guid> Order, IReadOnlyList<Guid> Byes);
