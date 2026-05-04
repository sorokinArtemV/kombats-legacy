using System.Net;
using System.Net.Http.Json;
using Kombats.Bff.Application.Errors;
using Microsoft.Extensions.Logging;

namespace Kombats.Bff.Application.Clients;

public static class HttpClientHelper
{
    public static async Task<T?> SendAsync<T>(
        HttpClient httpClient,
        HttpMethod method,
        string path,
        object? body,
        string serviceName,
        ILogger logger,
        CancellationToken cancellationToken) where T : class
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to reach {Service} at {Path}", serviceName, path);
            throw new ServiceUnavailableException(serviceName);
        }

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        BffError error = await ErrorMapper.MapFromResponseAsync(response, serviceName, logger, cancellationToken);
        throw new BffServiceException(response.StatusCode, error);
    }
}
