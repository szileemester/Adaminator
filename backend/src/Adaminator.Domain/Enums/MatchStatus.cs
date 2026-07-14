namespace Adaminator.Domain.Enums;

/// <summary>Lifecycle state of a single match.</summary>
public enum MatchStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Forfeit = 3
}
