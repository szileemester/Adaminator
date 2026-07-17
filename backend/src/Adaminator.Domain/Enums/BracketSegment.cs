namespace Adaminator.Domain.Enums;

/// <summary>
/// Which part of the bracket a match belongs to. Single Elimination uses <see cref="Winner"/> and
/// (optionally) <see cref="ThirdPlace"/>. <see cref="Loser"/>/<see cref="GrandFinal"/> are Double
/// Elimination only. <see cref="RoundRobin"/> is a flat, unrouted list of matches - no advancement.
/// </summary>
public enum BracketSegment
{
    Winner = 0,
    Loser = 1,
    GrandFinal = 2,
    ThirdPlace = 3,
    RoundRobin = 4
}
