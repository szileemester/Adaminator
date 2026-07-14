namespace Adaminator.Domain.Enums;

/// <summary>
/// Lifecycle state of a tournament: Planned -&gt; Running -&gt; Finished.
/// </summary>
public enum TournamentStatus
{
    Planned = 0,
    Running = 1,
    Finished = 2
}
