using Kombats.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;

internal sealed record ExecuteMatchmakingTickCommand(string Variant, int MaxPairsPerTick = 64)
    : ICommand<MatchmakingTickResult>;
