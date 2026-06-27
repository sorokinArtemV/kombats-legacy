using Kombats.Abstractions;

namespace Kombats.Players.Application.UseCases.GetPlayerProfile;

internal sealed record GetPlayerProfileQuery(Guid IdentityId) : IQuery<GetPlayerProfileQueryResponse>;
