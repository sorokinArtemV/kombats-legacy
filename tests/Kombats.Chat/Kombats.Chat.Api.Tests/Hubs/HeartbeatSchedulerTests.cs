using FluentAssertions;
using Kombats.Chat.Api.Hubs;
using Kombats.Chat.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Api.Tests.Hubs;

/// <summary>
/// Direct coverage of the heartbeat tick path. The 30-second timer cadence itself
/// is not driven in CI (would slow the suite by 30 s); instead the testable
/// <see cref="HeartbeatScheduler.TickAsync"/> seam is invoked once and the
/// defensive failure path is exercised against a throwing <see cref="IPresenceStore"/>.
/// </summary>
public sealed class HeartbeatSchedulerTests
{
    private static (HeartbeatScheduler scheduler, IPresenceStore presence) Build(IPresenceStore? presence = null)
    {
        var p = presence ?? Substitute.For<IPresenceStore>();
        var services = new ServiceCollection();
        services.AddScoped(_ => p);
        var provider = services.BuildServiceProvider();
        var scheduler = new HeartbeatScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<HeartbeatScheduler>.Instance,
            TimeProvider.System);
        return (scheduler, p);
    }

    [Fact]
    public async Task Tick_CallsHeartbeatAsyncOnPresenceStore()
    {
        var (scheduler, presence) = Build();
        var id = Guid.NewGuid();

        await scheduler.TickAsync(id);

        await presence.Received(1).HeartbeatAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_SwallowsExceptions()
    {
        var presence = Substitute.For<IPresenceStore>();
        presence.HeartbeatAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("redis down"));

        var (scheduler, _) = Build(presence);

        Func<Task> act = () => scheduler.TickAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync(
            "the heartbeat timer must never crash the process when presence calls fail");
    }

    [Fact]
    public void StartStop_DoesNotThrow_AndIsIdempotent()
    {
        var (scheduler, _) = Build();
        var id = Guid.NewGuid();

        scheduler.Start("conn-1", id);
        scheduler.Stop("conn-1");
        // Stop a connection that was never started — must be a safe no-op.
        scheduler.Stop("conn-unknown");

        scheduler.Dispose();
    }
}
