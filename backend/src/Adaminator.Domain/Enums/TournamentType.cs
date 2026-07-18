namespace Adaminator.Domain.Enums;

/// <summary>
/// The elimination structure used by a tournament.
/// </summary>
public enum TournamentType
{
    SingleElimination = 0,
    DoubleElimination = 1,
    RoundRobin = 2,

    /// <summary>
    /// Two-stage (TI-style): a per-group round-robin group stage, then a manually-started double
    /// elimination playoff seeded from the group standings (each group's top half enters the Winner
    /// Bracket, its bottom half the Loser Bracket).
    /// </summary>
    GroupStagePlayoff = 3
}
