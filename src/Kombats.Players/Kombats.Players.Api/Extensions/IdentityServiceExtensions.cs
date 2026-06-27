using Kombats.Players.Api.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Kombats.Players.Api.Extensions;

public static class IdentityServiceExtensions
{
    public static IServiceCollection AddCurrentIdentity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentIdentityProvider, HttpCurrentIdentityProvider>();
        return services;
    }
}
