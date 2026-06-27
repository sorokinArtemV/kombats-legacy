namespace Kombats.Bff.Application.Errors;

public sealed record BffError(string Code, string Message, object? Details = null);

public sealed record BffErrorResponse(BffError Error);
