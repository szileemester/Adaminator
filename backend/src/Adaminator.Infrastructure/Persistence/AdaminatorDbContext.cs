using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Adaminator.Infrastructure.Persistence;

public class AdaminatorDbContext : DbContext
{
    public AdaminatorDbContext(DbContextOptions<AdaminatorDbContext> options) : base(options)
    {
    }

    public DbSet<Tournament> Tournaments => Set<Tournament>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Match> Matches => Set<Match>();

    /// <summary>The house Unmatched ladder - a single overwritten row, unrelated to tournaments.</summary>
    public DbSet<UnmatchedScoreboard> UnmatchedScoreboard => Set<UnmatchedScoreboard>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdaminatorDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchChangedTournaments();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Brings a tournament row into any save that changed only its children, so the row-version check
    /// actually runs.
    ///
    /// The concurrency token lives on the tournament, and EF only puts it in the WHERE clause when a
    /// tournament column is dirty. Almost nothing dirties one: completing a match, undoing it, starting
    /// the playoffs, pairing the next Swiss round and replacing the roster all write children only, so
    /// those saves were leaving the token untested - exactly the writes it was added for. Two requests
    /// could then both draw a playoff from the same group stage and leave the tournament with two Grand
    /// Finals, which every later bracket read fails on. Marking the root modified restores the intended
    /// behaviour: the second writer loses on the version check and gets a 409.
    ///
    /// It hangs off the context rather than a repository so that every save is covered, including one
    /// made through a repository that knows nothing about tournaments.
    /// </summary>
    private void TouchChangedTournaments()
    {
        // Materialised once: each ChangeTracker.Entries() call re-runs change detection over the whole
        // tracked graph, which for a full aggregate is thousands of entities.
        var entries = ChangeTracker.Entries().ToList();
        var changed = new HashSet<Guid>();
        Dictionary<Guid, Guid>? tournamentByMatch = null;

        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Participant participant:
                    changed.Add(participant.TournamentId);
                    break;
                case Match match:
                    changed.Add(match.TournamentId);
                    break;
                case ScoreEntry scoreEntry:
                    // A score entry has no navigation to its match, so the owner is looked up by id.
                    // Built on first use: a Best-of-5 result produces ten changed entries (five deleted,
                    // five added) and every one of them would otherwise rescan the tracked matches.
                    tournamentByMatch ??= entries
                        .Select(e => e.Entity)
                        .OfType<Match>()
                        .ToDictionary(m => m.Id, m => m.TournamentId);

                    if (tournamentByMatch.TryGetValue(scoreEntry.MatchId, out var tournamentId))
                    {
                        changed.Add(tournamentId);
                    }

                    break;
            }
        }

        if (changed.Count == 0)
        {
            return;
        }

        foreach (var entry in entries)
        {
            // An added root carries its children in the same INSERT; only an untouched existing one needs it.
            if (entry.Entity is Tournament tournament
                && entry.State == EntityState.Unchanged
                && changed.Contains(tournament.Id))
            {
                entry.State = EntityState.Modified;
            }
        }
    }
}
