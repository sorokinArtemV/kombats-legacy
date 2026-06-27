using FluentAssertions;
using Kombats.Chat.Api.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kombats.Chat.Api.Tests.Endpoints;

/// <summary>
/// Readiness registration smoke — verifies /health/ready honestly covers every hard runtime
/// dependency of the delivered chat functionality: Postgres, Redis, RabbitMQ. Pre-merge fix
/// for Chat v1 final-gate review: readiness must not omit Redis or RabbitMQ.
/// </summary>
public sealed class HealthReadinessRegistrationTests : IClassFixture<ChatApiFactory>
{
    private readonly ChatApiFactory _factory;

    public HealthReadinessRegistrationTests(ChatApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Readiness_IncludesPostgresRedisAndRabbitMq()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;

        var names = options.Registrations.Select(r => r.Name).ToHashSet();

        names.Should().Contain("postgresql");
        names.Should().Contain("redis");
        names.Should().Contain("rabbitmq");
    }
}
