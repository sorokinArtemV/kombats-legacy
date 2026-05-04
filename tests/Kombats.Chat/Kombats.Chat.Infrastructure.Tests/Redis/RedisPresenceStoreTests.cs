using FluentAssertions;
using Kombats.Chat.Infrastructure.Redis;
using Kombats.Chat.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Redis;

[Collection(RedisCollection.Name)]
public sealed class RedisPresenceStoreTests(RedisFixture redisFixture)
{
    private RedisPresenceStore CreateStore()
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        return new RedisPresenceStore(mux, NullLogger<RedisPresenceStore>.Instance);
    }

    private async Task FlushDb()
    {
        var mux = ConnectionMultiplexer.Connect(redisFixture.AdminConnectionString);
        var server = mux.GetServers()[0];
        await server.FlushDatabaseAsync(2);
    }

    [Fact]
    public async Task Connect_FirstConnection_ReturnsTrue()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        bool isFirst = await store.ConnectAsync(id, "Player1", CancellationToken.None);

        isFirst.Should().BeTrue();
    }

    [Fact]
    public async Task Connect_SecondConnection_ReturnsFalse()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        await store.ConnectAsync(id, "Player1", CancellationToken.None);
        bool isFirst = await store.ConnectAsync(id, "Player1", CancellationToken.None);

        isFirst.Should().BeFalse();
    }

    [Fact]
    public async Task Disconnect_LastConnection_ReturnsTrue()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        await store.ConnectAsync(id, "Player1", CancellationToken.None);
        bool isLast = await store.DisconnectAsync(id, CancellationToken.None);

        isLast.Should().BeTrue();
    }

    [Fact]
    public async Task Disconnect_NotLastConnection_ReturnsFalse()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        await store.ConnectAsync(id, "Player1", CancellationToken.None);
        await store.ConnectAsync(id, "Player1", CancellationToken.None);

        bool isLast = await store.DisconnectAsync(id, CancellationToken.None);

        isLast.Should().BeFalse();
    }

    [Fact]
    public async Task MultiTab_TwoConnects_DisconnectOne_StillOnline_DisconnectSecond_Offline()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        // Two connections
        await store.ConnectAsync(id, "Player1", CancellationToken.None);
        await store.ConnectAsync(id, "Player1", CancellationToken.None);

        // First disconnect — still online
        bool isLast1 = await store.DisconnectAsync(id, CancellationToken.None);
        isLast1.Should().BeFalse();

        bool online = await store.IsOnlineAsync(id, CancellationToken.None);
        online.Should().BeTrue();

        // Second disconnect — offline
        bool isLast2 = await store.DisconnectAsync(id, CancellationToken.None);
        isLast2.Should().BeTrue();

        bool onlineAfter = await store.IsOnlineAsync(id, CancellationToken.None);
        onlineAfter.Should().BeFalse();
    }

    [Fact]
    public async Task Disconnect_AfterTtlExpiry_NoNegativeRefcount_NoFalseLastConnection()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        // Disconnect without connect — refs key doesn't exist (simulates post-TTL state)
        bool isLast = await store.DisconnectAsync(id, CancellationToken.None);

        // Must be false: player is already offline in Redis, so callers must not
        // emit a spurious "offline" broadcast from this teardown path.
        isLast.Should().BeFalse();

        // Verify no negative refcount and no dangling presence/online entries.
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        var db = mux.GetDatabase(2);
        var refs = await db.StringGetAsync($"chat:presence:refs:{id}");
        refs.HasValue.Should().BeFalse();

        var presence = await db.StringGetAsync($"chat:presence:{id}");
        presence.HasValue.Should().BeFalse();

        bool online = await store.IsOnlineAsync(id, CancellationToken.None);
        online.Should().BeFalse();
    }

    [Fact]
    public async Task Disconnect_AfterRefsTtlExpiry_ButPresenceDangling_CleansUpAndReturnsFalse()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        // Simulate the real race: refs key expired, but the online ZSET entry
        // and the presence blob haven't been swept yet (Redis evicts keys
        // lazily at different moments). Disconnect must clean both WITHOUT
        // claiming this was the last connection.
        var mux = ConnectionMultiplexer.Connect(redisFixture.ConnectionString);
        var db = mux.GetDatabase(2);
        await db.SortedSetAddAsync("chat:presence:online", id.ToString(), 0);
        await db.StringSetAsync($"chat:presence:{id}", "{\"name\":\"Stale\",\"connectedAtUnixMs\":0}");

        bool isLast = await store.DisconnectAsync(id, CancellationToken.None);

        isLast.Should().BeFalse();

        (await store.IsOnlineAsync(id, CancellationToken.None)).Should().BeFalse();
        (await db.StringGetAsync($"chat:presence:{id}")).HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task Heartbeat_RenewsTtls()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        await store.ConnectAsync(id, "Player1", CancellationToken.None);
        await store.HeartbeatAsync(id, CancellationToken.None);

        // Player should still be online
        bool online = await store.IsOnlineAsync(id, CancellationToken.None);
        online.Should().BeTrue();
    }

    [Fact]
    public async Task Heartbeat_NoRefKey_NoOp()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        // Heartbeat without connect — should be a no-op (I5 guard)
        await store.HeartbeatAsync(id, CancellationToken.None);

        bool online = await store.IsOnlineAsync(id, CancellationToken.None);
        online.Should().BeFalse();
    }

    [Fact]
    public async Task GetOnlinePlayers_ReturnsConnectedPlayers()
    {
        await FlushDb();
        var store = CreateStore();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await store.ConnectAsync(id1, "Alice", CancellationToken.None);
        await store.ConnectAsync(id2, "Bob", CancellationToken.None);

        var players = await store.GetOnlinePlayersAsync(100, 0, CancellationToken.None);

        players.Should().HaveCount(2);
        players.Should().Contain(p => p.DisplayName == "Alice");
        players.Should().Contain(p => p.DisplayName == "Bob");
    }

    [Fact]
    public async Task GetOnlineCount_ReturnsCorrectCount()
    {
        await FlushDb();
        var store = CreateStore();

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        await store.ConnectAsync(id1, "Alice", CancellationToken.None);
        await store.ConnectAsync(id2, "Bob", CancellationToken.None);

        long count = await store.GetOnlineCountAsync(CancellationToken.None);

        count.Should().Be(2);
    }

    [Fact]
    public async Task GetOnlinePlayers_Pagination_Works()
    {
        await FlushDb();
        var store = CreateStore();

        for (int i = 0; i < 5; i++)
        {
            await store.ConnectAsync(Guid.NewGuid(), $"Player{i}", CancellationToken.None);
        }

        var page1 = await store.GetOnlinePlayersAsync(3, 0, CancellationToken.None);
        var page2 = await store.GetOnlinePlayersAsync(3, 3, CancellationToken.None);

        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    [Fact]
    public async Task IsOnline_ConnectedPlayer_ReturnsTrue()
    {
        await FlushDb();
        var store = CreateStore();
        var id = Guid.NewGuid();

        await store.ConnectAsync(id, "Player1", CancellationToken.None);

        bool online = await store.IsOnlineAsync(id, CancellationToken.None);
        online.Should().BeTrue();
    }

    [Fact]
    public async Task IsOnline_NotConnectedPlayer_ReturnsFalse()
    {
        await FlushDb();
        var store = CreateStore();

        bool online = await store.IsOnlineAsync(Guid.NewGuid(), CancellationToken.None);
        online.Should().BeFalse();
    }
}
