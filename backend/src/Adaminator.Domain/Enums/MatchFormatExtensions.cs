namespace Adaminator.Domain.Enums;

/// <summary>Win-count math derived from a <see cref="MatchFormat"/>'s underlying BO-number.</summary>
public static class MatchFormatExtensions
{
    /// <summary>Number of game/set wins one participant needs to win the match (odd formats only; not used for a draw-capable format).</summary>
    public static int RequiredWins(this MatchFormat format) => ((int)format + 1) / 2;

    /// <summary>The most games/sets a match of this format can ever be decided in.</summary>
    public static int MaxGames(this MatchFormat format) => (int)format;

    /// <summary>An even format (only <see cref="MatchFormat.Bo2"/>) plays every game and may end level, so the match can be a draw.</summary>
    public static bool AllowsDraw(this MatchFormat format) => (int)format % 2 == 0;
}
