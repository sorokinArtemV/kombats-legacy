using Kombats.Abstractions;
using Kombats.Matchmaking.Api.Extensions;
using Kombats.Matchmaking.Api.Identity;
using Kombats.Matchmaking.Application.UseCases.GetQueueStatus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kombats.Matchmaking.Api.Endpoints.Queue;

internal sealed class GetQueueStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("api/v1/matchmaking/queue/status", async (
                ICurrentIdentityProvider identity,
                IQueryHandler<GetQueueStatusQuery, QueueStatusResult> handler,
                CancellationToken ct) =>
            {
                var subjectResult = identity.GetRequiredSubject();
                if (subjectResult.IsFailure) return subjectResult.ToProblem();

                var result = await handler.HandleAsync(
                    new GetQueueStatusQuery(subjectResult.Value), ct);

                return result.Match(
                    value => Results.Ok(new QueueStatusDto(
                        value.Status.ToString(),
                        value.MatchId,
                        value.BattleId,
                        value.MatchState?.ToString())),
                    failure => result.ToProblem());
            })
            .RequireAuthorization()
            .WithTags("Queue")
            .Produces<QueueStatusDto>(200)
            .ProducesProblem(401);
    }
}
