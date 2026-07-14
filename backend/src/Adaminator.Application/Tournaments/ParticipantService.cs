using Adaminator.Application.Common;
using Adaminator.Domain.Entities;

namespace Adaminator.Application.Tournaments;

/// <summary>Participant management use cases (allowed only while the tournament is Planned).</summary>
public class ParticipantService
{
    private readonly ITournamentRepository _repository;

    public ParticipantService(ITournamentRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<ParticipantDto>> ListAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        return tournament.Participants.ToOrderedDtos();
    }

    public async Task<ParticipantDto> AddAsync(Guid tournamentId, AddParticipantRequest request, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        var participant = tournament.AddParticipant(request.Name);
        await _repository.SaveChangesAsync(cancellationToken);
        return participant.ToDto();
    }

    public async Task<ParticipantDto> RenameAsync(Guid tournamentId, Guid participantId, RenameParticipantRequest request, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.RenameParticipant(participantId, request.Name);
        await _repository.SaveChangesAsync(cancellationToken);
        return tournament.Participants.First(p => p.Id == participantId).ToDto();
    }

    public async Task RemoveAsync(Guid tournamentId, Guid participantId, CancellationToken cancellationToken = default)
    {
        var tournament = await LoadAsync(tournamentId, cancellationToken);
        tournament.RemoveParticipant(participantId);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<Tournament> LoadAsync(Guid tournamentId, CancellationToken cancellationToken) =>
        await _repository.GetByIdAsync(tournamentId, cancellationToken)
        ?? throw new NotFoundException($"Tournament '{tournamentId}' was not found.");
}
