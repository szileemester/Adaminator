namespace Adaminator.Domain.Enums;

/// <summary>Win-count math derived from a <see cref="MatchFormat"/>'s underlying BO-number.</summary>
public static class MatchFormatExtensions
{
    /// <summary>Number of game/set wins one participant needs to win the match.</summary>
    public static int RequiredWins(this MatchFormat format) => ((int)format + 1) / 2;

    /// <summary>The most games/sets a match of this format can ever be decided in.</summary>
    public static int MaxGames(this MatchFormat format) => (int)format;
}
