using FluentAssertions;
using Kombats.Battle.Api.Endpoints;
using Kombats.Battle.Infrastructure.Realtime.SignalR;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Kombats.Battle.Api.Tests;

/// <summary>
/// Verifies Battle API endpoint structure and SignalR hub existence.
/// </summary>
public sealed class EndpointStructureTests
{

    [Fact]
    public void BattleHub_ExistsAndExtendsHub()
    {
        typeof(BattleHub).Should().BeAssignableTo<Hub>();
    }

    [Fact]
    public void BattleHub_IsInInfrastructureLayer()
    {
        // BattleHub lives in Infrastructure as a port implementation (SignalR adapter)
        typeof(BattleHub).Namespace.Should().Contain("Infrastructure.Realtime.SignalR");
    }
}
