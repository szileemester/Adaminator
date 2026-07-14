using Adaminator.Domain.Entities;

namespace Adaminator.Application.Tournaments;

/// <summary>
/// Persistence boundary for the <see cref="Tournament"/> aggregate.
/// Implemented in the Infrastructure layer.
/// </summary>
public interface ITournamentRepository
{
    Task<Tournament?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Tournament?> GetByPublicTokenAsync(string publicToken, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tournament>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Tournament tournament, CancellationToken cancellationToken = default);

    void Remove(Tournament tournament);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
