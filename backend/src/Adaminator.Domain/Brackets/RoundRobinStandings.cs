using Adaminator.Domain.Entities;

namespace Adaminator.Domain.Brackets;

/// <summary>A participant's win-loss record within a round-robin (or one group of a group stage).</summary>
public readonly record struct RoundRobinStanding(Guid ParticipantId, int Wins, int Losses)
{
    public int Played => Wins + Losses;
}

/// <summary>
/// Pure round-robin ranking, the single source of truth for both the displayed standings
/// (see <see cref="Adaminator.Domain"/> consumers in the Application layer) and the Group Stage +
/// Playoff seeding. Ranks by wins desc, then fewer losses, then name (ordinal, case-insensitive) for
/// a fully deterministic order - the spec defines no head-to-head tiebreaker.
/// </summary>
public static class RoundRobinStandings
{
    /// <summary>
    /// Ranks <paramref name="participants"/> by their record in <paramref name="matches"/>. Callers pass
    /// their existing id-to-participant lookup (used only for the name tiebreaker) rather than having one
    /// rebuilt per call - the projection ranks once per group off a single tournament-wide map.
    /// </summary>
    public static List<RoundRobinStanding> Rank(
        IEnumerable<Match> matches, IReadOnlyCollection<Participant> participants, IReadOnlyDictionary<Guid, Participant> roster)
    {
        var wins = new Dictionary<Guid, int>();
        var losses = new Dictionary<Guid, int>();

        foreach (var match in matches)
        {
            if (match.WinnerId is not { } winnerId || match.LoserId is not { } loserId)
            {
                continue;
            }

            wins[winnerId] = wins.GetValueOrDefault(winnerId) + 1;
            losses[loserId] = losses.GetValueOrDefault(loserId) + 1;
        }

        return participants
            .Select(p => new RoundRobinStanding(p.Id, wins.GetValueOrDefault(p.Id), losses.GetValueOrDefault(p.Id)))
            .OrderByDescending(r => r.Wins)
            .ThenBy(r => r.Losses)
            .ThenBy(r => roster[r.ParticipantId].Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
