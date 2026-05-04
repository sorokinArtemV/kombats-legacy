using Kombats.Abstractions;

namespace Kombats.Matchmaking.Application.UseCases.TimeoutStaleMatches;

/// <summary>
/// Command to timeout matches stuck in BattleCreateRequested or BattleCreated state.
/// </summary>
internal sealed record TimeoutStaleMatchesCommand(int TimeoutSeconds, int BattleCreatedTimeoutSeconds) : ICommand<int>;
