using Adaminator.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Adaminator.Api.Controllers;

public record LoginRequest(string Password);

public record LoginResponse(string Token, DateTimeOffset ExpiresAt);

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly AdminOptions _adminOptions;
    private readonly JwtTokenService _tokenService;

    public AuthController(IOptions<AdminOptions> adminOptions, JwtTokenService tokenService)
    {
        _adminOptions = adminOptions.Value;
        _tokenService = tokenService;
    }

    /// <summary>Exchanges the admin password for a bearer token.</summary>
    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(_adminOptions.Password))
        {
            return Problem(
                title: "Admin password not configured",
                detail: "The server has no admin password configured.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Constant-time comparison to avoid leaking the password length/content via timing.
        if (!CryptographicEquals(request.Password, _adminOptions.Password))
        {
            return Unauthorized(new { message = "Invalid password." });
        }

        var (token, expiresAt) = _tokenService.CreateAdminToken();
        return Ok(new LoginResponse(token, expiresAt));
    }

    private static bool CryptographicEquals(string? a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a ?? string.Empty);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            PadTo(bytesA, 64), PadTo(bytesB, 64)) && bytesA.Length == bytesB.Length;
    }

    private static byte[] PadTo(byte[] source, int length)
    {
        var result = new byte[length];
        Array.Copy(source, result, Math.Min(source.Length, length));
        return result;
    }
}
