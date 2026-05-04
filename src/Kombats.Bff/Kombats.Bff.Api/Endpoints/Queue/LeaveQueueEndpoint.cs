using Kombats.Bff.Api.Models.Responses;
using Kombats.Bff.Application.Clients;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Bff.Api.Endpoints.Queue;

public sealed class LeaveQueueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/queue/leave", async (
                LeaveQueueBffRequest? request,
                IMatchmakingClient matchmakingClient,
                CancellationToken cancellationToken) =>
            {
                var result = await matchmakingClient.LeaveQueueAsync(request?.ConnectionRef, cancellationToken);

                return Results.Ok(new LeaveQueueResponse(
                    LeftQueue: !result.Searching && result.MatchId is null,
                    MatchId: result.MatchId,
                    BattleId: result.BattleId));
            })
            .RequireAuthorization()
            .WithTags("Queue")
            .Produces<LeaveQueueResponse>()
            .ProducesProblem(401);
    }
}

public sealed record LeaveQueueBffRequest(string? ConnectionRef);
