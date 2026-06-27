using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Kombats.Messaging.Filters;
using Kombats.Messaging.Naming;
using Kombats.Messaging.Options;

namespace Kombats.Messaging.DependencyInjection;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds MassTransit messaging infrastructure with EF Core transactional outbox and inbox.
    /// All services must use this entry point — outbox is mandatory per architecture (AD-01).
    /// </summary>
    /// <typeparam name="TDbContext">The service's DbContext type for outbox/inbox persistence</typeparam>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration root</param>
    /// <param name="serviceName">Service name used in endpoint naming (e.g., "battle", "matchmaking")</param>
    /// <param name="configureConsumers">Action to register MassTransit consumers (supports explicit or assembly-scanning registration)</param>
    /// <param name="configure">Optional action to configure entity name mappings</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMessaging<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<IBusRegistrationConfigurator> configureConsumers,
        Action<MessagingBuilder>? configure = null)
        where TDbContext : DbContext
    {
        var messagingSection = configuration.GetSection(MessagingOptions.SectionName);
        services.Configure<MessagingOptions>(messagingSection);
        services.AddOptions<MessagingOptions>().Bind(messagingSection).ValidateOnStart();

        var options = new MessagingOptions();
        messagingSection.Bind(options);
        ValidateRequiredOptions(options);

        var builder = new MessagingBuilder();
        configure?.Invoke(builder);
        var entityNameMap = builder.BuildEntityNameMap(configuration);

        services.AddSingleton(entityNameMap);

        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(options.Outbox.QueryDelaySeconds);
            });

            x.AddConfigureEndpointsCallback((registrationContext, name, endpointConfigurator) =>
            {
                endpointConfigurator.UseEntityFrameworkOutbox<TDbContext>(registrationContext);
            });

            configureConsumers(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                var messagingOptions = context.GetRequiredService<IOptions<MessagingOptions>>().Value;
                var entityNameMapInstance = context.GetRequiredService<Dictionary<Type, string>>();

                cfg.Host(messagingOptions.RabbitMq.Host, messagingOptions.RabbitMq.Port,
                    messagingOptions.RabbitMq.VirtualHost, h =>
                    {
                        h.Username(messagingOptions.RabbitMq.Username);
                        h.Password(messagingOptions.RabbitMq.Password);
                        if (messagingOptions.RabbitMq.UseTls)
                        {
                            h.UseSsl(s => { });
                        }
                        h.Heartbeat(TimeSpan.FromSeconds(messagingOptions.RabbitMq.HeartbeatSeconds));
                    });

                if (messagingOptions.Scheduler.Enabled)
                {
                    cfg.UseDelayedMessageScheduler();
                }

                cfg.PrefetchCount = messagingOptions.Transport.PrefetchCount;
                cfg.ConcurrentMessageLimit = messagingOptions.Transport.ConcurrentMessageLimit;

                var entityNameFormatter = new EntityNameConvention(
                    entityNameMapInstance,
                    messagingOptions.Topology.EntityNamePrefix,
                    messagingOptions.Topology.UseKebabCase);
                cfg.MessageTopology.SetEntityNameFormatter(entityNameFormatter);

                cfg.UseMessageRetry(r =>
                {
                    r.Exponential(
                        messagingOptions.Retry.ExponentialCount,
                        TimeSpan.FromMilliseconds(messagingOptions.Retry.ExponentialMinMs),
                        TimeSpan.FromMilliseconds(messagingOptions.Retry.ExponentialMaxMs),
                        TimeSpan.FromMilliseconds(messagingOptions.Retry.ExponentialDeltaMs));
                });

                if (messagingOptions.Redelivery.Enabled)
                {
                    cfg.UseDelayedRedelivery(r =>
                    {
                        var intervals = messagingOptions.Redelivery.IntervalsSeconds
                            .Select(s => TimeSpan.FromSeconds(s))
                            .ToArray();
                        r.Intervals(intervals);
                    });
                }

                cfg.UseConsumeFilter(typeof(ConsumeLoggingFilter<>), context);

                var endpointFormatter = new CombatsEndpointNameFormatter(
                    serviceName,
                    false,
                    entityNameFormatter);

                cfg.ConfigureEndpoints(context, endpointFormatter);
            });
        });

        return services;
    }

    private static void ValidateRequiredOptions(MessagingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.RabbitMq.Host))
            throw new InvalidOperationException("Messaging:RabbitMq:Host is required");

        if (string.IsNullOrWhiteSpace(options.RabbitMq.Username))
            throw new InvalidOperationException("Messaging:RabbitMq:Username is required");

        if (string.IsNullOrWhiteSpace(options.RabbitMq.Password))
            throw new InvalidOperationException("Messaging:RabbitMq:Password is required");
    }
}
