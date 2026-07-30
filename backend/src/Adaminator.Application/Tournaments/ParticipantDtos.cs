namespace Adaminator.Application.Tournaments;

public record ParticipantDto(Guid Id, string Name, string? Emoji, int Seed, bool HasBye, int? GroupIndex);

public record AddParticipantRequest(string Name, string? Emoji = null);

/// <summary>Carries the emoji alongside the name, so one call covers a rename, an emoji change, or both.</summary>
public record UpdateParticipantRequest(string Name, string? Emoji = null);

/// <summary>
/// The complete roster, in display order. An entry with a null <see cref="RosterEntryRequest.Id"/> is
/// a new participant; anyone previously on the roster and absent here is removed.
/// </summary>
public record ReplaceRosterRequest(IReadOnlyList<RosterEntryRequest> Participants);

public record RosterEntryRequest(Guid? Id, string Name, string? Emoji = null);

/// <summary>Editable preview state: the full seed order and the selected bye recipients.</summary>
public record UpdateBracketRequest(IReadOnlyList<Guid> Order, IReadOnlyList<Guid> Byes);
