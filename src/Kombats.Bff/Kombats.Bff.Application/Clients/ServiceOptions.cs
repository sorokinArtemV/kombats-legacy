namespace Kombats.Bff.Application.Clients;

public sealed class ServiceOptions
{
    public required string BaseUrl { get; init; }
}

public sealed class ServicesOptions
{
    public required ServiceOptions Players { get; init; }
    public required ServiceOptions Matchmaking { get; init; }
    public required ServiceOptions Battle { get; init; }

    /// <summary>
    /// Chat service base URL. Optional during transitional rollout — Batch 5 wires it,
    /// older tests/configs that omit it still construct ServicesOptions cleanly.
    /// </summary>
    public ServiceOptions? Chat { get; init; }
}
