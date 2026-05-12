using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Kombats.Observability;

/// <summary>
/// Registers OpenTelemetry tracing + metrics for Kombats services and exposes a
/// process-wide <see cref="KombatsMetrics"/> singleton other code can inject.
///
/// The OTLP exporter is only attached when <c>OpenTelemetry:OtlpEndpoint</c> is
/// non-empty. With an empty endpoint the SDK is still wired up but discards data
/// — matching the existing behaviour of the per-service Program.cs that this
/// extension replaces, so deploys that don't ship an OTLP collector stay quiet.
/// </summary>
public static class KombatsObservabilityExtensions
{
    public const string ConfigSectionName = "OpenTelemetry";
    public const string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";
    public const string MetricExportIntervalKey = "OpenTelemetry:MetricExportIntervalMs";
    public const int DefaultMetricExportIntervalMs = 60_000;
    public const string ServiceNamespace = "kombats";

    public static IServiceCollection AddKombatsObservability(
        this IServiceCollection services,
        IConfiguration config,
        string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required.", nameof(serviceName));

        services.AddSingleton(new KombatsMetrics(serviceName));

        string? otlpEndpoint = config[OtlpEndpointKey];
        int metricExportIntervalMs = config.GetValue<int?>(MetricExportIntervalKey)
                                     ?? DefaultMetricExportIntervalMs;
        string deploymentEnvironment =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        string meterName = KombatsMetrics.MeterPrefix + serviceName;

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: serviceName, serviceNamespace: ServiceNamespace)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("deployment.environment", deploymentEnvironment),
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRedisInstrumentation()
                    .AddSource("Npgsql")
                    .AddSource("MassTransit")
                    .AddSource(meterName);

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter(meterName)
                    .AddMeter("MassTransit")
                    .AddMeter("Npgsql");

                if (!string.IsNullOrEmpty(otlpEndpoint))
                {
                    // Two-arg overload exposes PeriodicExportingMetricReaderOptions so
                    // we can override the SDK default (60s) — short load-test battles
                    // complete entirely within one default window and never surface in
                    // Prometheus. Tracing has no equivalent interval setting.
                    metrics.AddOtlpExporter((opt, readerOpt) =>
                    {
                        opt.Endpoint = new Uri(otlpEndpoint);
                        readerOpt.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = metricExportIntervalMs;
                    });
                }
            });

        return services;
    }
}
