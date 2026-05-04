using System.Reflection;
using FluentAssertions;
using Kombats.Bff.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class BattleHubTests
{
    [Fact]
    public void BattleHub_HasAuthorizeAttribute()
    {
        // The hub class must require authentication
        Type hubType = typeof(BattleHub);
        var authorizeAttribute = hubType.GetCustomAttribute<AuthorizeAttribute>();

        authorizeAttribute.Should().NotBeNull(
            "BattleHub must require authentication for all connections");
    }

    [Fact]
    public void BattleHub_HasJoinBattleMethod()
    {
        MethodInfo? method = typeof(BattleHub).GetMethod("JoinBattle");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<object>));

        ParameterInfo[] parameters = method.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(Guid));
        parameters[0].Name.Should().Be("battleId");
    }

    [Fact]
    public void BattleHub_HasSubmitTurnActionMethod()
    {
        MethodInfo? method = typeof(BattleHub).GetMethod("SubmitTurnAction");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));

        ParameterInfo[] parameters = method.GetParameters();
        parameters.Should().HaveCount(3);
        parameters[0].ParameterType.Should().Be(typeof(Guid));
        parameters[0].Name.Should().Be("battleId");
        parameters[1].ParameterType.Should().Be(typeof(int));
        parameters[1].Name.Should().Be("turnIndex");
        parameters[2].ParameterType.Should().Be(typeof(string));
        parameters[2].Name.Should().Be("actionPayload");
    }

    [Fact]
    public void BattleHub_OverridesOnDisconnectedAsync()
    {
        MethodInfo? method = typeof(BattleHub).GetMethod(
            "OnDisconnectedAsync",
            BindingFlags.Public | BindingFlags.Instance,
            [typeof(Exception)]);

        method.Should().NotBeNull();
        method!.DeclaringType.Should().Be(typeof(BattleHub),
            "BattleHub must override OnDisconnectedAsync to clean up downstream connections");
    }

    [Fact]
    public void BattleHub_IsSealed()
    {
        typeof(BattleHub).IsSealed.Should().BeTrue(
            "BattleHub should be sealed per coding standards");
    }

    [Fact]
    public void BattleHub_MethodSignatures_MatchBattleServiceHub()
    {
        // Verify the BFF hub exposes the same client-facing methods as Battle's hub
        // JoinBattle(Guid battleId) -> object (snapshot)
        // SubmitTurnAction(Guid battleId, int turnIndex, string actionPayload) -> void
        MethodInfo? joinMethod = typeof(BattleHub).GetMethod("JoinBattle");
        MethodInfo? submitMethod = typeof(BattleHub).GetMethod("SubmitTurnAction");

        joinMethod.Should().NotBeNull();
        submitMethod.Should().NotBeNull();

        // JoinBattle returns Task<object> (BFF doesn't reference Battle.Realtime.Contracts)
        joinMethod!.ReturnType.Should().Be(typeof(Task<object>));

        // SubmitTurnAction returns Task (void)
        submitMethod!.ReturnType.Should().Be(typeof(Task));
    }
}
