using Adaminator.Domain.Entities;
using FluentValidation;

namespace Adaminator.Application.Unmatched;

/// <summary>
/// Reads and overwrites the house Unmatched ladder. The scoreboard is created on first touch rather
/// than seeded by a migration, so an empty database and a reset one behave identically.
/// </summary>
public class UnmatchedService
{
    private readonly IUnmatchedRepository _repository;
    private readonly IValidator<UpdateUnmatchedScoreboardRequest> _validator;
    private readonly TimeProvider _timeProvider;

    public UnmatchedService(
        IUnmatchedRepository repository,
        IValidator<UpdateUnmatchedScoreboardRequest> validator,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _validator = validator;
        _timeProvider = timeProvider;
    }

    public async Task<UnmatchedScoreboardDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var scoreboard = await _repository.GetAsync(cancellationToken);
        // Nothing recorded yet: report the zero state without writing, so a public read stays a read.
        return (scoreboard ?? UnmatchedScoreboard.CreateEmpty(_timeProvider.GetUtcNow())).ToDto();
    }

    public async Task<UnmatchedScoreboardDto> UpdateAsync(
        UpdateUnmatchedScoreboardRequest request, CancellationToken cancellationToken = default)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);
        var scoreboard = await _repository.GetAsync(cancellationToken);
        if (scoreboard is null)
        {
            scoreboard = UnmatchedScoreboard.CreateEmpty(_timeProvider.GetUtcNow());
            await _repository.AddAsync(scoreboard, cancellationToken);
        }

        scoreboard.Update(
            request.FiukWins,
            request.LanyokWins,
            request.LastVictor,
            request.Picks.Select(p => (p.PlayerName, p.Character)).ToList(),
            _timeProvider.GetUtcNow());

        await _repository.SaveChangesAsync(cancellationToken);
        return scoreboard.ToDto();
    }
}

internal static class UnmatchedMappings
{
    public static UnmatchedScoreboardDto ToDto(this UnmatchedScoreboard scoreboard) => new(
        scoreboard.FiukWins,
        scoreboard.LanyokWins,
        scoreboard.LastVictor,
        scoreboard.Picks.Select(p => new UnmatchedPickDto(p.PlayerName, p.Character)).ToList(),
        scoreboard.UpdatedAt);
}
