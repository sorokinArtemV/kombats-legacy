using Kombats.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.ExecuteMatchmakingTick;

internal sealed record ExecuteMatchmakingTickCommand(string Variant) : ICommand<MatchmakingTickResult>;
