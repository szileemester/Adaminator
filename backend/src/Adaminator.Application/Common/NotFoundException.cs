namespace Adaminator.Application.Common;

/// <summary>
/// Raised when a requested resource does not exist. The API translates this into a 404 response.
/// </summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
