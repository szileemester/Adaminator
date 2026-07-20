namespace Adaminator.Domain.Enums;

/// <summary>
/// How a tournament resolves a standings tie that changes a real outcome (the Group Stage + Playoff
/// Upper/Lower split, or a Round Robin podium place). Only meaningful for <see cref="TournamentType.RoundRobin"/>
/// and <see cref="TournamentType.GroupStagePlayoff"/>.
/// </summary>
public enum TiebreakerPolicy
{
    /// <summary>
    /// Try computed criteria first (head-to-head, then game differential) and only generate played
    /// tiebreaker matches when participants are still exactly equal. Fewest extra matches.
    /// </summary>
    ComputedThenMatch = 0,

    /// <summary>
    /// Any tie on the raw win-loss record that straddles a decision boundary goes straight to played
    /// tiebreaker matches; computed criteria only break a leftover cycle among the played results.
    /// </summary>
    AlwaysMatch = 1
}
