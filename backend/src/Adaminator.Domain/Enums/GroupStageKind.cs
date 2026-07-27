namespace Adaminator.Domain.Enums;

/// <summary>
/// Group Stage + Playoff only: how the group stage itself is played before the playoff is seeded.
/// </summary>
public enum GroupStageKind
{
    /// <summary>
    /// The roster is drawn into <see cref="Entities.Tournament.GroupCount"/> groups and each group plays a
    /// full round robin. Participants are seeded into the playoff level by level (all group winners,
    /// then all runners-up, …).
    /// </summary>
    RoundRobin = 0,

    /// <summary>
    /// One single Swiss pool over the whole roster - no groups. Each round pairs participants on a
    /// similar record without repeating an earlier meeting, and the final standings seed the playoff
    /// directly.
    /// </summary>
    Swiss = 1
}
