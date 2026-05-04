using Kombats.Abstractions;
using Kombats.Matchmaking.Api.Extensions;
using Kombats.Matchmaking.Api.Identity;
using Kombats.Matchmaking.Application.UseCases.LeaveQueue;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Matchmaking.Api.Endpoints.Queue;

internal sealed class LeaveQueueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/matchmaking/queue/leave", async (
                LeaveQueueRequest request,
                ICurrentIdentityProvider identity,
                ICommandHandler<LeaveQueueCommand, LeaveQueueResult> handler,
                CancellationToken ct) =>
            {
                var subjectResult = identity.GetRequiredSubject();
                if (subjectResult.IsFailure) return subjectResult.ToProblem();

                var variant = request.Variant ?? "default";
                // See JoinQueueEndpoint for the legacy-ref rationale. On leave,
                // a synthetic ref is functionally a no-op against presence
                // (the SREM matches no real member) — the queue/status removals
                // still happen via the existing handler path.
                var connectionRef = string.IsNullOrWhiteSpace(request.ConnectionRef)
                    ? $"legacy-{Guid.NewGuid()}"
                    : request.ConnectionRef;

                var result = await handler.HandleAsync(
                    new LeaveQueueCommand(subjectResult.Value, variant, connectionRef), ct);

                return result.Match(
                    value => value.Status switch
                    {
                        LeaveQueueStatus.AlreadyMatched => Results.Conflict(new
                        {
                            Searching = false,
                            value.MatchId,
                            value.BattleId
                        }),
                        _ => Results.Ok(new { Searching = false })
                    },
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags("Queue")
            .Produces(200)
            .Produces(409)
            .ProducesProblem(401);
    }
}

public sealed record LeaveQueueRequest(string? Variant, string? ConnectionRef = null);
