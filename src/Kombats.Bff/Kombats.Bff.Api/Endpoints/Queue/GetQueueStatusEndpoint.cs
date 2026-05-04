using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Queue;

public sealed class GetQueueStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/queue/status", async (
                IMatchmakingClient matchmakingClient,
                CancellationToken cancellationToken) =>
            {
                var result = await matchmakingClient.GetQueueStatusAsync(cancellationToken);

                if (result is null)
                {
                    return Results.Ok(new QueueStatusResponse(Status: "Idle"));
                }

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
