namespace Adaminator.Domain.Enums;

/// <summary>
/// How a Group Stage + Playoff group's matches are played and scored. Meaningful only for
/// <see cref="TournamentType.GroupStagePlayoff"/>.
/// </summary>
public enum GroupStageFormat
{
    /// <summary>Each pairing plays one decisive match in the tournament's default format; standings rank by match wins.</summary>
    Standard = 0,

    /// <summary>Each pairing plays a Best-of-2 (both games, a 1-1 is a draw); standings rank by total games won.</summary>
    BestOfTwo = 1
}
