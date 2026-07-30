using Adaminator.Domain.Entities;

namespace Adaminator.Application.Tournaments;

internal static class ParticipantMappings
{
    public static ParticipantDto ToDto(this Participant participant) =>
        new(participant.Id, participant.Name, participant.Emoji, participant.Seed, participant.HasBye, participant.GroupIndex);

    /// <summary>
    /// Always in roster order - the order the organizer added them in. The roster is a management
    /// list, not a seeding view, so it must neither reorder itself the moment a bracket is generated
    /// (which sorting by <see cref="ParticipantDto.Seed"/> would do) nor rearrange the list someone
    /// just typed (which sorting by name would do). The bracket preview sorts by seed itself.
    /// </summary>
    public static IReadOnlyList<ParticipantDto> ToOrderedDtos(this IEnumerable<Participant> participants) =>
        participants
            .OrderBy(p => p.Position)
            .Select(ToDto)
            .ToList();
}
