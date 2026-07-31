using Adaminator.Application.Unmatched;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Adaminator.Api.Controllers;

/// <summary>
/// The house Unmatched ladder. Reading is anonymous so the page can be shared with the other players
/// by link alone; writing still needs the admin login, because the page's own "hidden" editor is a
/// client-side gesture and could not protect an open endpoint from anyone who found the URL.
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
    [Authorize]
    public async Task<ActionResult<UnmatchedScoreboardDto>> Update(
        [FromBody] UpdateUnmatchedScoreboardRequest request, CancellationToken cancellationToken) =>
        Ok(await _service.UpdateAsync(request, cancellationToken));
}
