using FluentAssertions;
using Kombats.Messaging.Options;
using Xunit;

namespace Kombats.Messaging.Tests.Options;

public class MessagingOptionsDefaultsTests
{
    [Fact]
    public void Retry_defaults_match_target_configuration()
    {
        var options = new RetryOptions();

        options.ExponentialCount.Should().Be(5, "target: 5 attempts");
        options.ExponentialMinMs.Should().Be(200, "target: 200ms minimum");
        options.ExponentialMaxMs.Should().Be(5000, "target: 5000ms maximum");
    }

    [Fact]
    public void Redelivery_defaults_match_target_configuration()
    {
        var options = new RedeliveryOptions();

        options.Enabled.Should().BeTrue("redelivery is enabled by default");
        options.IntervalsSeconds.Should().BeEquivalentTo(
            new[] { 30, 120, 600 },
            "target: 30s, 120s, 600s redelivery intervals");
    }

    [Fact]
    public void Outbox_defaults_are_enabled()
    {
        var options = new OutboxOptions();

        options.Enabled.Should().BeTrue("outbox is mandatory per AD-01");
    }

    [Fact]
    public void Topology_defaults_use_combats_prefix_and_kebab_case()
    {
        var options = new TopologyOptions();

        options.EntityNamePrefix.Should().Be("combats");
        options.UseKebabCase.Should().BeTrue();
    }

    [Fact]
    public void Configuration_section_name_is_Messaging()
    {
        MessagingOptions.SectionName.Should().Be("Messaging");
    }
}
