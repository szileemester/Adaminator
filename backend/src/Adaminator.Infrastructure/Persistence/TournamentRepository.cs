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

    // The tournament row is brought into child-only saves by AdaminatorDbContext, so the row-version
    // check runs whichever repository does the saving.
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
