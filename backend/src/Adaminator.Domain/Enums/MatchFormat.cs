namespace Adaminator.Domain.Enums;

/// <summary>
/// The number of games or sets in a match. The odd formats are always decisive; <see cref="Bo2"/> is
/// even, so it plays both games and can end level (a draw) - it is used only for a Best-of-2 group
/// stage that ranks by games won, never in an elimination bracket.
/// </summary>
public enum MatchFormat
{
    Bo1 = 1,
    Bo2 = 2,
    Bo3 = 3,
    Bo5 = 5,
    Bo7 = 7
}
