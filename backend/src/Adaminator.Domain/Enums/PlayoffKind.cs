namespace Adaminator.Domain.Enums;

/// <summary>
/// Group Stage + Playoff only: the elimination structure the qualifiers play once the group stage is
/// over.
/// </summary>
public enum PlayoffKind
{
    /// <summary>
    /// The qualifiers split in half: the top half enters the Winner Bracket and the bottom half the
    /// Loser Bracket, converging on a Grand Final.
    /// </summary>
    DoubleElimination = 0,

    /// <summary>
    /// Every qualifier enters one bracket and a single loss eliminates. May carry a Third Place match.
    /// </summary>
    SingleElimination = 1
}
