using FluentAssertions;
using Kombats.Bff.Api.Hubs;
using Kombats.Bff.Application.Relay;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class HubContextBattleSenderTests
{
    [Fact]
    public void HubContextBattleSender_ImplementsIFrontendBattleSender()
    {
        typeof(HubContextBattleSender).Should().Implement<IFrontendBattleSender>();
    }

    [Fact]
    public void HubContextBattleSender_IsSealed()
    {
        typeof(HubContextBattleSender).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void HubContextBattleSender_RequiresIHubContext()
    {
        var ctors = typeof(HubContextBattleSender).GetConstructors();
        ctors.Should().HaveCount(1);

        var parameters = ctors[0].GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Name.Should().Contain("IHubContext",
            "HubContextBattleSender must be backed by IHubContext for stable out-of-scope usage");
    }
}
