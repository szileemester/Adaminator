using Adaminator.Domain.Entities;

namespace Adaminator.Application.Tournaments;

internal static class ParticipantMappings
{
    public static ParticipantDto ToDto(this Participant participant) =>
        new(participant.Id, participant.Name, participant.Emoji, participant.Seed, participant.HasBye, participant.GroupIndex);

    /// <summary>Seeded participants in seed order; unseeded ones alphabetically after them.</summary>
    public static IReadOnlyList<ParticipantDto> ToOrderedDtos(this IEnumerable<Participant> participants) =>
        participants
            .OrderBy(p => p.Seed == 0 ? int.MaxValue : p.Seed)
            .ThenBy(p => p.Name)
            .Select(ToDto)
            .ToList();
}
