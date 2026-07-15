using Adaminator.Application.Tournaments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adaminator.Api.Controllers;

[ApiController]
[Route("api/tournaments/{tournamentId:guid}/matches/{matchId:guid}")]
[Authorize]
public class MatchController : ControllerBase
{
    private readonly MatchService _service;

    public MatchController(MatchService service)
    {
        _service = service;
    }

    /// <summary>Saves a (possibly partial) detailed score; the match stays undecided.</summary>
    [HttpPut("result")]
    public async Task<ActionResult<BracketDto>> SaveResult(Guid tournamentId, Guid matchId, [FromBody] SaveMatchResultRequest request, CancellationToken cancellationToken)
    {
        var bracket = await _service.SaveResultAsync(tournamentId, matchId, request, cancellationToken);
        return Ok(bracket);
    }

    /// <summary>Saves the deciding detailed score and advances the winner.</summary>
    [HttpPost("complete")]
    public async Task<ActionResult<BracketDto>> Complete(Guid tournamentId, Guid matchId, [FromBody] CompleteMatchRequest request, CancellationToken cancellationToken)
    {
        var bracket = await _service.CompleteAsync(tournamentId, matchId, request, cancellationToken);
        return Ok(bracket);
    }

    /// <summary>Completes the match by forfeit; the selected winner advances normally.</summary>
    [HttpPost("forfeit")]
    public async Task<ActionResult<BracketDto>> Forfeit(Guid tournamentId, Guid matchId, [FromBody] ForfeitMatchRequest request, CancellationToken cancellationToken)
    {
        var bracket = await _service.ForfeitAsync(tournamentId, matchId, request, cancellationToken);
        return Ok(bracket);
    }

    /// <summary>Reverts the chronologically latest completed/forfeited match.</summary>
    [HttpPost("undo")]
    public async Task<ActionResult<BracketDto>> Undo(Guid tournamentId, Guid matchId, CancellationToken cancellationToken)
    {
        var bracket = await _service.UndoAsync(tournamentId, matchId, cancellationToken);
        return Ok(bracket);
    }
}
