using FluentAssertions;
using Kombats.Bff.Api.Hubs;
using Kombats.Bff.Application.Relay;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class HubContextChatSenderTests
{
    [Fact]
    public void HubContextChatSender_ImplementsIFrontendChatSender()
    {
        typeof(HubContextChatSender).Should().Implement<IFrontendChatSender>();
    }

    [Fact]
    public void HubContextChatSender_IsSealed()
    {
        typeof(HubContextChatSender).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void HubContextChatSender_RequiresIHubContext()
    {
        var ctors = typeof(HubContextChatSender).GetConstructors();
        ctors.Should().HaveCount(1);
        var parameters = ctors[0].GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Name.Should().Contain("IHubContext",
            "HubContextChatSender must be backed by IHubContext for stable out-of-scope usage");
    }
}
