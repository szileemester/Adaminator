using Adaminator.Application.Common;
using Adaminator.Domain.Entities;
using Adaminator.Domain.Enums;
using FluentValidation;

namespace Adaminator.Application.Tournaments;

/// <summary>Match result entry use cases: save partial score, complete, forfeit, undo.</summary>
public class MatchService
{
    private readonly ITournamentRepository _repository;
    private readonly IValidator<MatchScoreRequest> _scoreValidator;
    private readonly IValidator<ForfeitMatchRequest> _forfeitValidator;
    private readonly TimeProvider _timeProvider;

    public MatchService(
        ITournamentRepository repository,
        IValidator<MatchScoreRequest> scoreValidator,
        IValidator<ForfeitMatchRequest> forfeitValidator,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _scoreValidator = scoreValidator;
        _forfeitValidator = forfeitValidator;
        _timeProvider = timeProvider;
    }

    public async Task<BracketDto> SaveResultAsync(Guid tournamentId, Guid matchId, MatchScoreRequest request, CancellationToken cancellationToken = default)
    {
        await _scoreValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.SaveMatchResult(matchId, request.MatchFormat, request.ScoreType, ToEntries(request.Entries));
        await _repository.SaveChangesAsync(cancellationToken);
        return BracketProjection.Build(tournament);
    }

    public async Task<BracketDto> CompleteAsync(Guid tournamentId, Guid matchId, MatchScoreRequest request, CancellationToken cancellationToken = default)
    {
        await _scoreValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.CompleteMatch(matchId, request.MatchFormat, request.ScoreType, ToEntries(request.Entries), _timeProvider.GetUtcNow());
        await _repository.SaveChangesAsync(cancellationToken);
        return BracketProjection.Build(tournament);
    }

    public async Task<BracketDto> ForfeitAsync(Guid tournamentId, Guid matchId, ForfeitMatchRequest request, CancellationToken cancellationToken = default)
    {
        await _forfeitValidator.ValidateAndThrowAsync(request, cancellationToken);
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.ForfeitMatch(matchId, request.WinnerId, _timeProvider.GetUtcNow());
        await _repository.SaveChangesAsync(cancellationToken);
        return BracketProjection.Build(tournament);
    }

    public async Task<BracketDto> UndoAsync(Guid tournamentId, Guid matchId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.UndoMatch(matchId);
        await _repository.SaveChangesAsync(cancellationToken);
        return BracketProjection.Build(tournament);
    }

    private static IReadOnlyList<ScoreEntryInput> ToEntries(IReadOnlyList<ScoreEntryInputDto> entries) =>
        entries.Select(e => new ScoreEntryInput(e.ScoreA, e.ScoreB, e.ParticipantAWon)).ToList();

    private async Task<Tournament> LoadAsync(Guid tournamentId, CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(tournamentId, cancellationToken)
        ?? throw new NotFoundException($"Tournament '{tournamentId}' was not found.");
}
