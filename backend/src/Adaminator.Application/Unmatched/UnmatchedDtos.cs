using Adaminator.Domain.Enums;

namespace Adaminator.Application.Unmatched;

public record UnmatchedPickDto(string PlayerName, string Character);

/// <summary>The whole scoreboard - totals, who took the last game, and what everyone is playing.</summary>
public record UnmatchedScoreboardDto(
    int FiukWins,
    int LanyokWins,
    UnmatchedTeam? LastVictor,
    IReadOnlyList<UnmatchedPickDto> Picks,
    DateTimeOffset UpdatedAt);

/// <summary>A full replacement of the scoreboard; there is nothing to patch, since nothing accumulates.</summary>
public record UpdateUnmatchedScoreboardRequest(
    int FiukWins,
    int LanyokWins,
    UnmatchedTeam? LastVictor,
    IReadOnlyList<UnmatchedPickDto> Picks);
