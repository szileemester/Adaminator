using Adaminator.Application.Tournaments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adaminator.Api.Controllers;

[ApiController]
[Route("api/public")]
[AllowAnonymous]
public class PublicController : ControllerBase
{
    private readonly TournamentService _service;

    public PublicController(TournamentService service)
    {
        _service = service;
    }

    /// <summary>Read-only public tournament view, addressed by opaque token (FR-PUBLIC-001).</summary>
    [HttpGet("tournaments/{token}")]
    public async Task<ActionResult<PublicTournamentDto>> GetByToken(string token, CancellationToken cancellationToken)
    {
        var tournament = await _service.GetByPublicTokenAsync(token, cancellationToken);
        return tournament is null ? NotFound() : Ok(tournament);
    }
}
