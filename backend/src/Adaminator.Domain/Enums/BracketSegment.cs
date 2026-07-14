namespace Adaminator.Domain.Enums;

/// <summary>
/// Which part of the bracket a match belongs to. Single Elimination uses
/// <see cref="Winner"/> and (optionally) <see cref="ThirdPlace"/>. The remaining values are
/// reserved for Double Elimination in a later milestone.
/// </summary>
public enum BracketSegment
{
    Winner = 0,
    Loser = 1,
    GrandFinal = 2,
    ThirdPlace = 3
}
