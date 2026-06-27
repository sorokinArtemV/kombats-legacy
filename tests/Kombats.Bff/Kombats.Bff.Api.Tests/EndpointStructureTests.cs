using System.Reflection;
using FluentAssertions;
using Kombats.Bff.Api.Endpoints;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class EndpointStructureTests
{
    private static readonly Assembly ApiAssembly = typeof(IEndpoint).Assembly;

    [Fact]
    public void AllEndpointTypes_ImplementIEndpoint()
    {
        IEnumerable<Type> endpointTypes = ApiAssembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                     && t.IsAssignableTo(typeof(IEndpoint)));

        endpointTypes.Should().NotBeEmpty("BFF should have at least one endpoint");

        foreach (Type type in endpointTypes)
        {
            type.Should().BeAssignableTo<IEndpoint>(
                $"{type.Name} should implement IEndpoint");
        }
    }

    [Fact]
    public void AllEndpointTypes_AreDiscoverableViaScan()
    {
        Type[] endpointTypes = ApiAssembly
            .DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                        && type.IsAssignableTo(typeof(IEndpoint)))
            .Cast<Type>()
            .ToArray();

        // BFF-0 health + BFF-1A (3 Players) + BFF-1B (3 Matchmaking) = 7 endpoints
        endpointTypes.Should().HaveCountGreaterThanOrEqualTo(7,
            "BFF should have at least 7 endpoints (1 health + 3 Players + 3 Matchmaking)");
    }

    [Theory]
    [InlineData("OnboardEndpoint")]
    [InlineData("SetCharacterNameEndpoint")]
    [InlineData("AllocateStatsEndpoint")]
    [InlineData("JoinQueueEndpoint")]
    [InlineData("LeaveQueueEndpoint")]
    [InlineData("GetQueueStatusEndpoint")]
    [InlineData("HealthEndpoint")]
    public void ExpectedEndpoint_ExistsInAssembly(string endpointTypeName)
    {
        Type? endpointType = ApiAssembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == endpointTypeName);

        endpointType.Should().NotBeNull($"Endpoint {endpointTypeName} should exist");
        endpointType.Should().BeAssignableTo<IEndpoint>();
    }

    [Fact]
    public void AllEndpointTypes_ArePublicOrSealed()
    {
        IEnumerable<Type> endpointTypes = ApiAssembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                     && t.IsAssignableTo(typeof(IEndpoint)));

        foreach (Type type in endpointTypes)
        {
            type.IsSealed.Should().BeTrue($"{type.Name} should be sealed");
        }
    }
}
