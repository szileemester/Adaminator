using Adaminator.Api.Infrastructure;
using Adaminator.Application.Unmatched;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Adaminator.Api.Controllers;

/// <summary>
/// The house Unmatched ladder. Both reading and writing are anonymous: the page is shared with the
/// other players by link alone, and whoever has the link is trusted to record the result. The only
/// thing standing between a visitor and a write is the editor's hidden gesture, which is a
/// convenience, not a credential - the domain's validation is the real guard on what can be stored.
/// </summary>
[ApiController]
[Route("api/unmatched")]
public class UnmatchedController : ControllerBase
{
    private readonly UnmatchedService _service;

    public UnmatchedController(UnmatchedService service)
    {
        _service = service;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<UnmatchedScoreboardDto>> Get(CancellationToken cancellationToken) =>
        Ok(await _service.GetAsync(cancellationToken));

    [HttpPut]
    [AllowAnonymous]
    // The only anonymous write in the API, so the only one a loop could sit on.
    [EnableRateLimiting(RateLimitPolicies.PublicWrite)]
    public async Task<ActionResult<UnmatchedScoreboardDto>> Update(
        [FromBody] UpdateUnmatchedScoreboardRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.UpdateAsync(request, cancellationToken));
}
