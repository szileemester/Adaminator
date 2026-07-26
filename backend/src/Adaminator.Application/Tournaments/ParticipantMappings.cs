using Adaminator.Domain.Entities;

namespace Adaminator.Application.Tournaments;

internal static class ParticipantMappings
{
    public static ParticipantDto ToDto(this Participant participant) =>
        new(participant.Id, participant.Name, participant.Emoji, participant.Seed, participant.HasBye, participant.GroupIndex);

    /// <summary>
    /// Always alphabetical by name - the roster is a management list, not a seeding view, so it
    /// shouldn't visibly reorder itself the moment a bracket is generated. Each DTO still carries its
    /// own <see cref="ParticipantDto.Seed"/>; the bracket preview sorts by that itself.
    /// </summary>
    public static IReadOnlyList<ParticipantDto> ToOrderedDtos(this IEnumerable<Participant> participants) =>
        participants
            .OrderBy(p => p.Name)
            .Select(ToDto)
            .ToList();
}
