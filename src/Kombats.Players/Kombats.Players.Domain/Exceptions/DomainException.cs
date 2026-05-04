namespace Kombats.Players.Domain.Exceptions;

/// <summary>
/// Typed domain exception with stable error codes for business rule violations.
/// </summary>
public sealed class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}

