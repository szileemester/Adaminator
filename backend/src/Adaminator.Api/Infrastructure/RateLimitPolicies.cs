namespace Adaminator.Api.Infrastructure;

/// <summary>
/// Names shared between the policy definitions in Program.cs and the actions that opt into them.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Per-IP throttle on the login endpoint, which guards a single shared password with no lockout.</summary>
    public const string Login = "login";

    /// <summary>Per-IP throttle on the endpoints that write without a login.</summary>
    public const string PublicWrite = "public-write";
}
