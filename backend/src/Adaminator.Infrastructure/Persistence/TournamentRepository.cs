using Adaminator.Application.Tournaments;
using Adaminator.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Adaminator.Infrastructure.Persistence;

public class TournamentRepository : ITournamentRepository
{
    private readonly AdaminatorDbContext _dbContext;

    public TournamentRepository(AdaminatorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // AsSplitQuery because participants and matches are sibling collections: one query would join them
    // against each other and repeat the whole tournament row once per (participant x match x game).
    public Task<Tournament?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _dbContext.Tournaments
            .Include(t => t.Participants)
            .Include(t => t.Matches).ThenInclude(m => m.ScoreEntries)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tournament?> GetByPublicTokenAsync(string publicToken, CancellationToken cancellationToken = default) =>
        _dbContext.Tournaments
            .AsNoTracking()
            .Include(t => t.Participants)
            .Include(t => t.Matches).ThenInclude(m => m.ScoreEntries)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.PublicToken == publicToken, cancellationToken);

    public async Task<IReadOnlyList<Tournament>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Tournaments
            .AsNoTracking()
            .Include(t => t.Participants)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default) =>
        await _dbContext.Tournaments.AddAsync(tournament, cancellationToken);

    public void Remove(Tournament tournament) => _dbContext.Tournaments.Remove(tournament);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchChangedAggregateRoots();
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Brings the tournament row itself into any save that changed only its children, so the row-version
    /// check actually runs.
    ///
    /// The concurrency token lives on the tournament, and EF only puts it in the WHERE clause when a
    /// tournament column is dirty. Almost nothing here dirties one: completing a match, undoing it,
    /// starting the playoffs, pairing the next Swiss round and replacing the roster all write children
    /// only, so those saves were leaving the token untested - exactly the writes it was added for. Two
    /// requests could then both draw a playoff from the same group stage and leave the tournament with
    /// two Grand Finals, which every later bracket read fails on. Marking the root modified restores the
    /// intended behaviour: the second writer loses on the version check and gets a 409.
    /// </summary>
    private void TouchChangedAggregateRoots()
    {
        var changed = new HashSet<Guid>();

        foreach (var entry in _dbContext.ChangeTracker.Entries())
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
                    // Entries are only ever written through their match, which is loaded with them.
                    if (_dbContext.ChangeTracker.Entries<Match>()
                            .FirstOrDefault(m => m.Entity.Id == scoreEntry.MatchId) is { } owner)
                    {
                        changed.Add(owner.Entity.TournamentId);
                    }

                    break;
            }
        }

        if (changed.Count == 0)
        {
            return;
        }

        foreach (var root in _dbContext.ChangeTracker.Entries<Tournament>())
        {
            // Added roots carry their children in the same INSERT; only an untouched existing one needs it.
            if (root.State == EntityState.Unchanged && changed.Contains(root.Entity.Id))
            {
                root.State = EntityState.Modified;
            }
        }
    }
}
