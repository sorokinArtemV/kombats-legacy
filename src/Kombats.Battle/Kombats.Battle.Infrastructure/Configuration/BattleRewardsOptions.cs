namespace Kombats.Battle.Infrastructure.Configuration;

/// <summary>
/// XP reward amounts surfaced on the BattleEnded SignalR payload so the result
/// screen can display a per-battle XP row immediately, without waiting for the
/// async Players handler.
///
/// IMPORTANT: these values MUST stay in sync with the WinnerXp / LoserXp
/// constants in
/// src/Kombats.Players/Kombats.Players.Application/Battles/HandleBattleCompletedCommand.cs.
/// Players is the source of truth for what is actually awarded; this Battle-side
/// copy only exists to feed the SignalR payload. If the values diverge, the
/// frontend will display XP that does not match what Players granted.
/// Future cleanup: extract to a shared rewards config service and remove this
/// duplication.
/// </summary>
public class BattleRewardsOptions
{
    public const string SectionName = "BattleRewards";

    public int WinXp { get; set; } = 10;
    public int LossXp { get; set; } = 5;
}