namespace Adaminator.Application.Tournaments;

public record ParticipantDto(Guid Id, string Name, int Seed, bool HasBye, int? GroupIndex);

public record AddParticipantRequest(string Name);

public record RenameParticipantRequest(string Name);

/// <summary>Editable preview state: the full seed order and the selected bye recipients.</summary>
public record UpdateBracketRequest(IReadOnlyList<Guid> Order, IReadOnlyList<Guid> Byes);
