using Adaminator.Application.Tournaments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adaminator.Api.Controllers;

[ApiController]
[Route("api/tournaments")]
[Authorize]
public class TournamentsController : ControllerBase
{
    private readonly TournamentService _service;

    public TournamentsController(TournamentService service)
    {
        _service = service;
    }

    /// <summary>Lists all tournaments (Planned, Running, Finished) for the dashboard.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TournamentSummaryDto>>> GetAll(CancellationToken cancellationToken)
    {
        var tournaments = await _service.GetAllAsync(cancellationToken);
        return Ok(tournaments);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TournamentDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var tournament = await _service.GetByIdAsync(id, cancellationToken);
        return tournament is null ? NotFound() : Ok(tournament);
    }

    [HttpPost]
    public async Task<ActionResult<TournamentDto>> Create([FromBody] CreateTournamentRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TournamentDto>> Update(Guid id, [FromBody] UpdateTournamentRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.UpdateAsync(id, request, cancellationToken);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _service.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
