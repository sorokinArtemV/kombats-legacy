namespace Kombats.Bff.Application.Errors;

public static class BffErrorCode
{
    public const string ServiceUnavailable = "service_unavailable";
    public const string CharacterNotFound = "character_not_found";
    public const string CharacterNotReady = "character_not_ready";
    public const string AlreadyInQueue = "already_in_queue";
    public const string NotInQueue = "not_in_queue";
    public const string InvalidRequest = "invalid_request";
    public const string InternalError = "internal_error";
    public const string Unauthorized = "unauthorized";
}
