using Adaminator.Application.Tournaments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adaminator.Api.Controllers;

[ApiController]
[Route("api/tournaments/{tournamentId:guid}")]
[Authorize]
public class BracketController : ControllerBase
{
    private readonly BracketService _service;

    public BracketController(BracketService service)
    {
        _service = service;
    }

    /// <summary>The bracket projected from matches (empty rounds until the tournament starts).</summary>
    [HttpGet("bracket")]
    public async Task<ActionResult<BracketDto>> GetBracket(Guid tournamentId, CancellationToken cancellationToken)
    {
        var bracket = await _service.GetBracketAsync(tournamentId, cancellationToken);
        return Ok(bracket);
    }

    /// <summary>Random seeding with a default bye selection.</summary>
    [HttpPost("bracket/generate")]
    public async Task<ActionResult<IReadOnlyList<ParticipantDto>>> Generate(Guid tournamentId, CancellationToken cancellationToken)
    {
        var participants = await _service.GenerateAsync(tournamentId, cancellationToken);
        return Ok(participants);
    }

    /// <summary>Save a manually edited preview (seed order + bye recipients).</summary>
    [HttpPut("bracket")]
    public async Task<ActionResult<IReadOnlyList<ParticipantDto>>> Update(Guid tournamentId, [FromBody] UpdateBracketRequest request, CancellationToken cancellationToken)
    {
        var participants = await _service.UpdateAsync(tournamentId, request, cancellationToken);
        return Ok(participants);
    }

    [HttpPost("start")]
    public async Task<ActionResult<TournamentDto>> Start(Guid tournamentId, CancellationToken cancellationToken)
    {
        var tournament = await _service.StartAsync(tournamentId, cancellationToken);
        return Ok(tournament);
    }
}
