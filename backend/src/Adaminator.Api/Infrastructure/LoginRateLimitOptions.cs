namespace Adaminator.Api.Infrastructure;

/// <summary>
/// Throttling for /api/auth/login. Configurable so integration tests - which run many logins in
/// seconds against a single shared test-server "connection" - can raise the limit instead of
/// tripping it; production keeps the tight default.
/// </summary>
public class LoginRateLimitOptions
{
    public const string SectionName = "RateLimiting:Login";

    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
}
