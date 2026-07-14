namespace Adaminator.Domain.Exceptions;

/// <summary>
/// Raised when a domain invariant or business rule is violated.
/// The API layer translates this into a 400-level validation response.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
