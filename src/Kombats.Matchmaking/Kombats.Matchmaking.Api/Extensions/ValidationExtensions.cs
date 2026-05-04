using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Kombats.Matchmaking.Api.Extensions;

public static class ValidationExtensions
{
    public static IServiceCollection AddMatchmakingValidation(this IServiceCollection services, Assembly assembly)
    {
        services.AddValidatorsFromAssembly(assembly);
        return services;
    }
}
