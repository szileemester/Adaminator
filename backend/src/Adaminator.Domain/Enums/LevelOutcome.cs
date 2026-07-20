namespace Adaminator.Domain.Enums;

/// <summary>
/// What happens to everyone who finishes at a given placement level of a Group Stage + Playoff group
/// (all group winners are one level, all runners-up the next, and so on). Determined purely by the
/// group sizes and the playoff capacity, so it is known as soon as the groups are drawn.
/// </summary>
public enum LevelOutcome
{
    /// <summary>The whole level seeds into the Winner Bracket.</summary>
    Upper = 0,

    /// <summary>The whole level seeds into the Loser Bracket.</summary>
    Lower = 1,

    /// <summary>The whole level falls outside the playoff capacity and is eliminated at the group stage.</summary>
    Eliminated = 2,

    /// <summary>
    /// The level straddles a boundary, so its members are competing for fewer slots than there are of
    /// them - they play a cross-group decider to settle the order between them.
    /// </summary>
    Contested = 3
}
