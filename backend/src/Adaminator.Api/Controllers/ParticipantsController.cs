using Adaminator.Application.Tournaments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adaminator.Api.Controllers;

[ApiController]
[Route("api/tournaments/{tournamentId:guid}/participants")]
[Authorize]
public class ParticipantsController : ControllerBase
{
    private readonly ParticipantService _service;

    public ParticipantsController(ParticipantService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ParticipantDto>>> List(Guid tournamentId, CancellationToken cancellationToken)
    {
        var participants = await _service.ListAsync(tournamentId, cancellationToken);
        return Ok(participants);
    }

    [HttpPost]
    public async Task<ActionResult<ParticipantDto>> Add(Guid tournamentId, [FromBody] AddParticipantRequest request, CancellationToken cancellationToken)
    {
        var participant = await _service.AddAsync(tournamentId, request, cancellationToken);
        return Ok(participant);
    }

    [HttpPut("{participantId:guid}")]
    public async Task<ActionResult<ParticipantDto>> Update(Guid tournamentId, Guid participantId, [FromBody] UpdateParticipantRequest request, CancellationToken cancellationToken)
    {
        var participant = await _service.UpdateAsync(tournamentId, participantId, request, cancellationToken);
        return Ok(participant);
    }

    [HttpDelete("{participantId:guid}")]
    public async Task<IActionResult> Remove(Guid tournamentId, Guid participantId, CancellationToken cancellationToken)
    {
        await _service.RemoveAsync(tournamentId, participantId, cancellationToken);
        return NoContent();
    }
}
