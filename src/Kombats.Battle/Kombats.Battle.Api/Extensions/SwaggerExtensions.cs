using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Kombats.Battle.Api.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddBattleApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Kombats Battle Service",
                    Version = "v1"
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

                document.Components.SecuritySchemes[JwtBearerDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = JwtBearerDefaults.AuthenticationScheme,
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Enter the JWT access token"
                };

                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document),
                        []
                    }
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static WebApplication UseBattleApiDocumentation(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options
                .WithTitle("Kombats Battle API")
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });

        return app;
    }
}
