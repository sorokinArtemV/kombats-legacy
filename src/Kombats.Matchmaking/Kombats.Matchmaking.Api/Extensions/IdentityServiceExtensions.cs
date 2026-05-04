using Kombats.Matchmaking.Api.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Kombats.Matchmaking.Api.Extensions;

public static class IdentityServiceExtensions
{
    public static IServiceCollection AddCurrentIdentity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentIdentityProvider, HttpCurrentIdentityProvider>();
        return services;
    }
}
