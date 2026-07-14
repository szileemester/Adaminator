namespace Adaminator.Api.Auth;

public class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// The single admin password. Supplied via configuration/environment and never committed.
    /// </summary>
    public string Password { get; set; } = string.Empty;
}
