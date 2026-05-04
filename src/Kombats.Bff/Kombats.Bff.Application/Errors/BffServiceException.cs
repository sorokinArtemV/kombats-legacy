using System.Net;

namespace Kombats.Bff.Application.Errors;

public sealed class BffServiceException(HttpStatusCode statusCode, BffError error) : Exception(error.Message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public BffError Error { get; } = error;
}

public sealed class ServiceUnavailableException(string serviceName)
    : Exception($"{serviceName} service is unavailable.")
{
    public string ServiceName { get; } = serviceName;
}
