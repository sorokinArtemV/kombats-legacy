namespace Kombats.Bff.Application.Clients;

public sealed class ResilienceOptions
{
    public int TotalRequestTimeoutSeconds { get; set; } = 30;
    public int AttemptTimeoutSeconds { get; set; } = 10;
    public int RetryMaxAttempts { get; set; } = 3;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;
}
