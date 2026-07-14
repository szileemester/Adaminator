using Adaminator.Application.Common;
using Adaminator.Domain.Entities;
using FluentValidation;

namespace Adaminator.Application.Tournaments;

/// <summary>
/// Use-case entry point for tournament management (create / edit / delete / read).
/// All business validation runs here or in the domain, never only in the UI (NFR reliability).
/// </summary>
public class TournamentService
{
    private readonly ITournamentRepository _repository;
    private readonly IValidator<CreateTournamentRequest> _createValidator;
    private readonly IValidator<UpdateTournamentRequest> _updateValidator;
    private readonly TimeProvider _timeProvider;

    public TournamentService(
        ITournamentRepository repository,
        IValidator<CreateTournamentRequest> createValidator,
        IValidator<UpdateTournamentRequest> updateValidator,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _timeProvider = timeProvider;
    }

    public async Task<TournamentDto> CreateAsync(CreateTournamentRequest request, CancellationToken cancellationToken = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var tournament = Tournament.Create(
            request.Name,
            request.Date,
            request.Notes,
            request.Type,
            request.DefaultMatchFormat,
            request.ThirdPlaceEnabled,
            _timeProvider.GetUtcNow());

        await _repository.AddAsync(tournament, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return tournament.ToDto();
    }

    public async Task<TournamentDto> UpdateAsync(Guid id, UpdateTournamentRequest request, CancellationToken cancellationToken = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, cancellationToken);

        var tournament = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Tournament '{id}' was not found.");

        tournament.UpdateDetails(
            request.Name,
            request.Date,
            request.Notes,
            request.Type,
            request.DefaultMatchFormat,
            request.ThirdPlaceEnabled);

        await _repository.SaveChangesAsync(cancellationToken);

        return tournament.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tournament = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Tournament '{id}' was not found.");

        _repository.Remove(tournament);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TournamentSummaryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var tournaments = await _repository.GetAllAsync(cancellationToken);
        return tournaments.Select(t => t.ToSummary()).ToList();
    }

    public async Task<TournamentDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tournament = await _repository.GetByIdAsync(id, cancellationToken);
        return tournament?.ToDto();
    }

    public async Task<PublicTournamentDto?> GetByPublicTokenAsync(string publicToken, CancellationToken cancellationToken = default)
    {
        var tournament = await _repository.GetByPublicTokenAsync(publicToken, cancellationToken);
        return tournament?.ToPublic();
    }
}
