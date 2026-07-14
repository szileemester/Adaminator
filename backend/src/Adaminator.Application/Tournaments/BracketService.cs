using Adaminator.Application.Common;
using Adaminator.Domain.Brackets;
using Adaminator.Domain.Entities;

namespace Adaminator.Application.Tournaments;

/// <summary>Bracket preview, seeding and tournament-start use cases.</summary>
public class BracketService
{
    private readonly ITournamentRepository _repository;

    public BracketService(ITournamentRepository repository)
    {
        _repository = repository;
    }

    /// <summary>Random seeding with a default bye selection (top seeds), per FR-BRACKET-001.</summary>
    public async Task<IReadOnlyList<ParticipantDto>> GenerateAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);

        var shuffled = tournament.Participants
            .Select(p => p.Id)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        var requiredByes = SingleEliminationBracket.ComputeRequiredByes(tournament.Participants.Count);
        var defaultByes = shuffled.Take(requiredByes).ToList();

        tournament.ApplySeeding(shuffled, defaultByes);
        await _repository.SaveChangesAsync(cancellationToken);
        return tournament.Participants.ToOrderedDtos();
    }

    /// <summary>Persists a manually edited preview (reordered seeds and/or bye selection).</summary>
    public async Task<IReadOnlyList<ParticipantDto>> UpdateAsync(Guid tournamentId, UpdateBracketRequest request, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.ApplySeeding(request.Order, request.Byes);
        await _repository.SaveChangesAsync(cancellationToken);
        return tournament.Participants.ToOrderedDtos();
    }

    public async Task<TournamentDto> StartAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.Start();
        await _repository.SaveChangesAsync(cancellationToken);
        return tournament.ToDto();
    }

    public async Task<BracketDto> GetBracketAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        return BracketProjection.Build(tournament);
    }

    private async Task<Tournament> LoadAsync(Guid tournamentId, CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(tournamentId, cancellationToken)
        ?? throw new NotFoundException($"Tournament '{tournamentId}' was not found.");
}
