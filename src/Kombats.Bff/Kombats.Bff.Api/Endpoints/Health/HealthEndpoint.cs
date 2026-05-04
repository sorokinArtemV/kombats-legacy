using System.Net;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kombats.Bff.Api.Endpoints.Health;

public sealed class HealthEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/health", HandleAsync)
            .AllowAnonymous()
            .WithTags("Health");
    }

    private static async Task<IResult> HandleAsync(
        IOptions<ServicesOptions> servicesOptions,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        ServicesOptions options = servicesOptions.Value;

        var services = new Dictionary<string, string>
        {
            ["players"] = options.Players.BaseUrl,
            ["matchmaking"] = options.Matchmaking.BaseUrl,
            ["battle"] = options.Battle.BaseUrl
        };

        var results = new Dictionary<string, string>();
        bool allHealthy = true;

        await Parallel.ForEachAsync(services, cancellationToken, async (kvp, ct) =>
        {
            string status;
            try
            {
                using HttpClient client = httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(kvp.Value);
                client.Timeout = TimeSpan.FromSeconds(5);

                HttpResponseMessage response = await client.GetAsync("/health", ct);
                status = response.IsSuccessStatusCode ? "healthy" : "unhealthy";
            }
            catch
            {
                status = "unhealthy";
            }

            lock (results)
            {
                results[kvp.Key] = status;
            }

            if (status != "healthy")
            {
                allHealthy = false;
            }
        });

        string overallStatus = allHealthy ? "healthy" : "degraded";
        bool anyHealthy = results.Values.Any(v => v == "healthy");

        if (!anyHealthy)
        {
            return Results.Json(
                new { status = "unhealthy", services = results },
                statusCode: (int)HttpStatusCode.ServiceUnavailable);
        }

        int statusCode = allHealthy ? 200 : 503;
        return Results.Json(new { status = overallStatus, services = results }, statusCode: statusCode);
    }
}
