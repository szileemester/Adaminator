namespace Adaminator.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "Adaminator";
    public string Audience { get; set; } = "Adaminator";
    public int ExpiryMinutes { get; set; } = 480;
}
