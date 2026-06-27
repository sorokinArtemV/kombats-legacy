using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.ReadModels;

namespace Kombats.Battle.Application.Ports;

/// <summary>
/// Service for processing raw action payloads into canonical action representations.
/// Handles wire-level parsing (JSON), protocol validation, and semantic validation.
/// Invalid payloads are normalized to NoAction with appropriate quality/reason.
/// </summary>
public interface IActionIntake
{
    /// <summary>
    /// Processes a raw action payload into a canonical action command.
    /// Invalid payloads result in NoAction with appropriate quality/reason.
    /// </summary>
    PlayerActionCommand ProcessAction(
        Guid battleId,
        Guid playerId,
        int clientTurnIndex,
        string? rawPayload,
        BattleSnapshot battleState);
}
