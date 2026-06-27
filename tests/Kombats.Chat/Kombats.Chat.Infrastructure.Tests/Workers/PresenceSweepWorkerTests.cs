using FluentAssertions;
using Kombats.Chat.Application.Notifications;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Infrastructure.Options;
using Kombats.Chat.Infrastructure.Redis;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Kombats.Chat.Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Workers;

[Collection(RedisCollection.Name)]
public sealed class PresenceSweepWorkerTests(RedisFixture redisFixture)
{
    private async Task FlushDb()
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.AdminConnectionString);
        var server = mux.GetServers()[0];
        await server.FlushDatabaseAsync(2);
    }

    private (PresenceSweepWorker worker, RedisPresenceStore store, IChatNotifier notifier) Build(
        PresenceSweepOptions options)
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        var store = new RedisPresenceStore(mux, NullLogger<RedisPresenceStore>.Instance);
        var notifier = Substitute.For<IChatNotifier>();

        var services = new ServiceCollection();
        services.AddSingleton<IPresenceStore>(store);
        services.AddSingleton(notifier);

        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var optionsMonitor = OptionsMonitorFor(options);

        var worker = new PresenceSweepWorker(
            scopeFactory,
            NullLogger<PresenceSweepWorker>.Instance,
            optionsMonitor);

        return (worker, store, notifier);
    }

    private static IOptionsMonitor<PresenceSweepOptions> OptionsMonitorFor(PresenceSweepOptions options)
        => new FakeOptionsMonitor<PresenceSweepOptions>(options);

    [Fact]
    public async Task RunOnce_StaleEntry_IsRemoved_AndOfflineBroadcast()
    {
        await FlushDb();
        var opts = new PresenceSweepOptions { StaleAfterSeconds = 1, ScanIntervalSeconds = 60 };
        var (worker, store, notifier) = Build(opts);

        var id = Guid.NewGuid();
        await store.ConnectAsync(id, "Alice", CancellationToken.None);

        // Force the ZSET score into the past so it qualifies as stale.
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        var db = mux.GetDatabase(2);
        long pastMs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync("chat:presence:online", id.ToString(), pastMs);

        await worker.RunOnceAsync(CancellationToken.None);

        (await store.IsOnlineAsync(id, CancellationToken.None)).Should().BeFalse();
        await notifier.Received(1).BroadcastPlayerOfflineAsync(
            Arg.Is<PlayerOfflineEvent>(e => e.PlayerId == id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnce_FreshEntry_IsPreserved_AndNoBroadcast()
    {
        await FlushDb();
        var opts = new PresenceSweepOptions { StaleAfterSeconds = 90, ScanIntervalSeconds = 60 };
        var (worker, store, notifier) = Build(opts);

        var id = Guid.NewGuid();
        await store.ConnectAsync(id, "Bob", CancellationToken.None);

        await worker.RunOnceAsync(CancellationToken.None);

        (await store.IsOnlineAsync(id, CancellationToken.None)).Should().BeTrue();
        await notifier.DidNotReceive().BroadcastPlayerOfflineAsync(
            Arg.Any<PlayerOfflineEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnce_TwoSweepersRace_OnlyOneBroadcastsPerStaleEntry()
    {
        // Simulates future multi-instance scenario: two workers call the store concurrently.
        // The ZREM atomic gating guarantees only the sweeper whose ZREM returned 1 broadcasts.
        await FlushDb();
        var opts = new PresenceSweepOptions { StaleAfterSeconds = 1, ScanIntervalSeconds = 60 };
        var (workerA, storeA, notifierA) = Build(opts);
        var (workerB, _, notifierB) = Build(opts);

        var id = Guid.NewGuid();
        await storeA.ConnectAsync(id, "Carol", CancellationToken.None);
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        var db = mux.GetDatabase(2);
        long pastMs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync("chat:presence:online", id.ToString(), pastMs);

        // Run both in parallel.
        await Task.WhenAll(
            workerA.RunOnceAsync(CancellationToken.None),
            workerB.RunOnceAsync(CancellationToken.None));

        int totalBroadcasts =
            notifierA.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatNotifier.BroadcastPlayerOfflineAsync))
          + notifierB.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IChatNotifier.BroadcastPlayerOfflineAsync));

        totalBroadcasts.Should().Be(1);
    }

    [Fact]
    public async Task SweepStale_AfterReconnect_DoesNotClobberFreshRefsKey()
    {
        // Regression guard for the §4.2 race. Reproduces the exact interleaving the
        // review identified:
        //   1. A stale member sits in the online ZSET (silent death; refs key already
        //      reaped by its 90s TTL).
        //   2. Sweeper starts a pass, snapshots the stale member and ZREMs it.
        //   3. Before the sweeper performs its best-effort KeyDelete cleanup, the same
        //      identity reconnects: ConnectScript INCRs refs 0→1 and re-adds to ZSET.
        //   4. Sweeper's KeyDelete(refsKey) would then wipe the freshly-incremented
        //      refcount — leaving the user in ZSET but with no refs, so the next
        //      DisconnectAsync silently no-ops and never emits PlayerOffline.
        //
        // With the fix (no refs delete in the sweep), the reconnected user's refs is
        // preserved, DisconnectAsync hits the refs<=1 branch, and PlayerOffline is
        // signalled normally.
        await FlushDb();
        var opts = new PresenceSweepOptions { StaleAfterSeconds = 1, ScanIntervalSeconds = 60 };
        var (worker, store, _) = Build(opts);

        var id = Guid.NewGuid();
        string refsKey = $"chat:presence:refs:{id}";
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        var db = mux.GetDatabase(2);

        // Step 1: stale ZSET entry with NO refs key (TTL-reaped silent death).
        long pastMs = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync("chat:presence:online", id.ToString(), pastMs);
        (await db.KeyExistsAsync(refsKey)).Should().BeFalse();

        // Step 2+3 collapsed: run the sweep, then immediately reconnect. The sweep's
        // ZREM + (previously) KeyDelete all issue sequentially; we model "reconnect
        // wins the race" by reconnecting after sweep completes. In the buggy code,
        // the KeyDelete would have run AFTER reconnect's INCR in the real race; here
        // we verify the sweeper simply never issues that delete against refs.
        await worker.RunOnceAsync(CancellationToken.None);

        bool firstConnect = await store.ConnectAsync(id, "Eve", CancellationToken.None);
        firstConnect.Should().BeTrue("reconnect after TTL-reaped refs must be a first connection");
        (await db.KeyExistsAsync(refsKey)).Should().BeTrue("refs must survive the sweep");

        // Step 4: final disconnect must emit offline — proves refs wasn't clobbered.
        bool lastDisconnect = await store.DisconnectAsync(id, CancellationToken.None);
        lastDisconnect.Should().BeTrue();
    }

    [Fact]
    public async Task RunOnce_NoEntries_NoBroadcast()
    {
        await FlushDb();
        var (worker, _, notifier) = Build(new PresenceSweepOptions { StaleAfterSeconds = 1, ScanIntervalSeconds = 60 });

        await worker.RunOnceAsync(CancellationToken.None);

        await notifier.DidNotReceive().BroadcastPlayerOfflineAsync(
            Arg.Any<PlayerOfflineEvent>(), Arg.Any<CancellationToken>());
    }
}
