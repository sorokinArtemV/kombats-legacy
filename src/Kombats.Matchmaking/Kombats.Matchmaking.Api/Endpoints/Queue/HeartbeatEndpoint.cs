using Kombats.Abstractions;
using Kombats.Matchmaking.Api.Extensions;
using Kombats.Matchmaking.Api.Identity;
using Kombats.Matchmaking.Application.UseCases.Heartbeat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Matchmaking.Api.Endpoints.Queue;

internal sealed class HeartbeatEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/matchmaking/queue/heartbeat", async (
                HeartbeatRequest request,
                ICurrentIdentityProvider identity,
                ICommandHandler<HeartbeatCommand> handler,
                CancellationToken ct) =>
            {
                var subjectResult = identity.GetRequiredSubject();
                if (subjectResult.IsFailure) return subjectResult.ToProblem();

                if (string.IsNullOrWhiteSpace(request.ConnectionRef))
                {
                    return Results.BadRequest(new { error = new { code = "Heartbeat.MissingConnectionRef", message = "connectionRef is required." } });
                }

                var result = await handler.HandleAsync(
                    new HeartbeatCommand(subjectResult.Value, request.ConnectionRef), ct);

                return result.IsSuccess ? Results.Ok() : result.ToProblem();
            })
            .RequireAuthorization()
            .WithTags("Queue")
            .Produces(200)
            .ProducesProblem(400)
            .ProducesProblem(401);
    }
}

public sealed record HeartbeatRequest(string? ConnectionRef);
