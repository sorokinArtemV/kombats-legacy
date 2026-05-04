using FluentAssertions;
using Kombats.Messaging.DependencyInjection;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Kombats.Messaging.Tests.Integration;

public class OutboxIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RabbitMqContainer _rabbitMq = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        _rabbitMq = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-management")
            .Build();

        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Outbox_publish_delivers_message_to_consumer()
    {
        TestConsumer.Reset();

        // Parse connection details from Testcontainer
        var connUri = new Uri(_rabbitMq.GetConnectionString());
        var userInfo = connUri.UserInfo.Split(':');

        var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Messaging:RabbitMq:Host"] = connUri.Host,
                    ["Messaging:RabbitMq:Port"] = connUri.Port.ToString(),
                    ["Messaging:RabbitMq:Username"] = userInfo[0],
                    ["Messaging:RabbitMq:Password"] = userInfo[1],
                    ["Messaging:RabbitMq:VirtualHost"] = "/",
                    ["Messaging:Scheduler:Enabled"] = "false",
                    ["Messaging:Redelivery:Enabled"] = "false",
                    ["Messaging:Outbox:QueryDelaySeconds"] = "1",
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<TestDbContext>(options =>
                {
                    options.UseNpgsql(_postgres.GetConnectionString());
                });

                services.AddMessaging<TestDbContext>(
                    context.Configuration,
                    "test",
                    x => { x.AddConsumer<TestConsumer>(); },
                    builder => { builder.MapEntityName<TestEvent>("combats.test-event"); });
            })
            .Build();

        // Create database tables (outbox + inbox entities)
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await host.StartAsync();

        try
        {
            // Wait for the bus to fully connect
            var busControl = host.Services.GetRequiredService<IBusControl>();
            using var readyCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await busControl.WaitForHealthStatus(BusHealthStatus.Healthy, readyCts.Token);

            // Publish a message — with UseBusOutbox(), IPublishEndpoint writes to outbox table
            var testEvent = new TestEvent(Guid.NewGuid(), "integration-test", 1);

            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

                await publishEndpoint.Publish(testEvent);
                await db.SaveChangesAsync();
            }

            // Wait for outbox delivery service to poll and deliver the message
            var received = await TestConsumer.MessageReceived.WaitAsync(TimeSpan.FromSeconds(30));

            received.Should().BeTrue("message should be delivered via outbox within timeout");
            TestConsumer.ReceivedMessages.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(testEvent);
        }
        finally
        {
            await host.StopAsync();
            host.Dispose();
        }
    }
}
