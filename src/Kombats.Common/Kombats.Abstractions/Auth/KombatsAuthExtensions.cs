using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kombats.Abstractions.Auth;

public static class KombatsAuthExtensions
{
    /// <summary>
    /// Configures JWT Bearer authentication with Keycloak.
    /// Reads authority and audience from the "Keycloak" configuration section.
    /// </summary>
    public static IServiceCollection AddKombatsAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var authority = configuration["Keycloak:Authority"]
            ?? throw new InvalidOperationException("Keycloak:Authority configuration is required.");

        var audience = configuration["Keycloak:Audience"]
            ?? throw new InvalidOperationException("Keycloak:Audience configuration is required.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = false;
                // Required so downstream service calls (e.g. Chat → Players HTTP fallback)
                // can retrieve the inbound token via HttpContext.GetTokenAsync("access_token").
                options.SaveToken = true;

                options.TokenValidationParameters.NameClaimType = "preferred_username";
            });

        services.AddAuthorization();

        return services;
    }
}
