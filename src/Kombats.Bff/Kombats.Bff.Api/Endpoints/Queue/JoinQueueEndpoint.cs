using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Queue;

public sealed class JoinQueueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/queue/join", async (
                JoinQueueBffRequest? request,
                IMatchmakingClient matchmakingClient,
                CancellationToken cancellationToken) =>
            {
                var result = await matchmakingClient.JoinQueueAsync(request?.ConnectionRef, cancellationToken);

                return Results.Ok(new QueueStatusResponse(
                    Status: result.Status,
                    MatchId: result.MatchId,
                    BattleId: result.BattleId,
                    MatchState: result.MatchState));
            })
            .RequireAuthorization()
            .WithTags("Queue")
            .Produces<QueueStatusResponse>()
            .ProducesProblem(401);
    }
}

public sealed record JoinQueueBffRequest(string? ConnectionRef);
