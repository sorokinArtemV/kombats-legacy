using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Queue;

public sealed class HeartbeatEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/queue/heartbeat", async (
                HeartbeatBffRequest request,
                IMatchmakingClient matchmakingClient,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.ConnectionRef))
                {
                    return Results.BadRequest(new { error = new { code = "Heartbeat.MissingConnectionRef", message = "connectionRef is required." } });
                }

                await matchmakingClient.HeartbeatAsync(request.ConnectionRef, cancellationToken);
                return Results.Ok();
            })
            .RequireAuthorization()
            .WithTags("Queue")
            .Produces(200)
            .ProducesProblem(400)
            .ProducesProblem(401);
    }
}

public sealed record HeartbeatBffRequest(string? ConnectionRef);
