using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Kombats.Battle.Application.Models;
using Kombats.Battle.Application.Ports;
using Kombats.Battle.Application.ReadModels;
using Microsoft.Extensions.Logging;

namespace Kombats.Battle.Application.UseCases.Lifecycle;

/// <summary>
/// Application service for battle lifecycle operations.
/// Orchestrates battle initialization and turn opening.
/// </summary>
public sealed class BattleLifecycleAppService
{
    private readonly IBattleStateStore _stateStore;
    private readonly IBattleRealtimeNotifier _notifier;
    private readonly IRulesetProvider _rulesetProvider;
    private readonly ISeedGenerator _seedGenerator;
    private readonly IClock _clock;
    private readonly ILogger<BattleLifecycleAppService> _logger;

    public BattleLifecycleAppService(
        IBattleStateStore stateStore,
        IBattleRealtimeNotifier notifier,
        IRulesetProvider rulesetProvider,
        ISeedGenerator seedGenerator,
        IClock clock,
        ILogger<BattleLifecycleAppService> logger)
    {
        _stateStore = stateStore;
        _notifier = notifier;
        _rulesetProvider = rulesetProvider;
        _seedGenerator = seedGenerator;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Handles battle creation: initializes battle state and opens turn 1.
    /// Convergent and idempotent: re-processing the same event always converges to correct state.
    /// Never leaves battle in ArenaOpen without an active turn.
    ///
    /// Uses a blind, convergent sequence:
    /// 1. TryInitializeBattleAsync (idempotent SETNX - ignore return value for flow decisions)
    /// 2. TryOpenTurnAsync(turnIndex=1) (idempotent Lua script - only succeeds if state exists,
    ///    not ended, LastResolvedTurnIndex==0, and Phase is ArenaOpen or Resolving)
    ///
    /// The TryOpenTurnScript is the convergence gate: it will return 0 if Turn 1 is already open
    /// (because Phase==TurnOpen and/or LastResolvedTurnIndex mismatch), so we only notify when
    /// it returns true (actual transition occurred).
    /// </summary>
    /// <returns>Ruleset version and seed used for this battle, or null if initialization failed (non-retryable error).</returns>
    public async Task<BattleInitializationResult?> HandleBattleCreatedAsync(
        Guid battleId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        CombatProfile profileA,
        CombatProfile profileB,
        string? playerAName,
        string? playerBName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling battle creation for BattleId: {BattleId}", battleId);

        _logger.LogInformation(
            "Using combat profiles for BattleId: {BattleId}. PlayerA={PlayerAId} (Str={StrA}, Sta={StaA}, Agi={AgiA}, Int={IntA}), PlayerB={PlayerBId} (Str={StrB}, Sta={StaB}, Agi={AgiB}, Int={IntB})",
            battleId, profileA.PlayerId, profileA.Strength, profileA.Stamina, profileA.Agility, profileA.Intuition,
            profileB.PlayerId, profileB.Strength, profileB.Stamina, profileB.Agility, profileB.Intuition);

        RulesetWithoutSeed rulesetWithoutSeed;
        try
        {
            rulesetWithoutSeed = _rulesetProvider.GetCurrentRuleset();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to get current ruleset for BattleId: {BattleId}. Configuration error. ACKing message to avoid infinite retries.",
                battleId);
            return null;
        }

        int battleSeed = _seedGenerator.GenerateSeed();

        // Build final domain Ruleset for this battle
        Ruleset domainRuleset = Ruleset.Create(
            version: rulesetWithoutSeed.Version,
            turnSeconds: rulesetWithoutSeed.TurnSeconds,
            noActionLimit: rulesetWithoutSeed.NoActionLimit,
            seed: battleSeed,
            balance: rulesetWithoutSeed.Balance);

        // Build initial state
        BattleDomainState initialState = BuildInitialState(
            battleId,
            matchId,
            playerAId,
            playerBId,
            domainRuleset,
            profileA,
            profileB);

        // Compute max HP from derived stats (same computation used for initial HP)
        var playerAStats = new PlayerStats(profileA.Strength, profileA.Stamina, profileA.Agility, profileA.Intuition);
        var playerBStats = new PlayerStats(profileB.Strength, profileB.Stamina, profileB.Agility, profileB.Intuition);
        int playerAMaxHp = CombatMath.ComputeDerived(playerAStats, domainRuleset.Balance).HpMax;
        int playerBMaxHp = CombatMath.ComputeDerived(playerBStats, domainRuleset.Balance).HpMax;

        // Convergent initialization: blind call to TryInitializeBattleAsync
        // This is idempotent (SETNX) - ignore return value for flow decisions
        await _stateStore.TryInitializeBattleAsync(battleId, initialState, playerAName, playerBName, playerAMaxHp, playerBMaxHp, cancellationToken);

        // Convergent turn opening: blind call to TryOpenTurnAsync
        // TryOpenTurnScript is the convergence gate - it will:
        // - Return 1 only if: state exists, not ended, LastResolvedTurnIndex==0, Phase is ArenaOpen or Resolving
        // - Return 0 if Turn 1 is already open (Phase==TurnOpen and/or LastResolvedTurnIndex mismatch)
        var turn1Deadline = BuildTurn1DeadlineUtc(domainRuleset);
        bool isTurnOpened = await _stateStore.TryOpenTurnAsync(battleId, 1, turn1Deadline, cancellationToken);

        // Only notify when Turn 1 was actually opened (TryOpenTurnAsync == true)
        // If it returns false, Turn 1 is already open or battle is in a different state (converged)
        if (isTurnOpened)
        {
            // Use the computed deadlineUtc we passed to TryOpenTurnAsync (Lua stores exactly the passed deadline)
            await _notifier.NotifyBattleReadyAsync(battleId, playerAId, playerBId, playerAName, playerBName, cancellationToken);
            await _notifier.NotifyTurnOpenedAsync(battleId, 1, turn1Deadline, cancellationToken);

            _logger.LogInformation(
                "Battle {BattleId} initialized and Turn 1 opened. RulesetVersion: {RulesetVersion}, Seed: {Seed}, Deadline: {DeadlineUtc}",
                battleId, domainRuleset.Version, battleSeed, turn1Deadline);
        }
        else
        {
            _logger.LogInformation(
                "Battle {BattleId} already has Turn 1 open or is in a different state (converged, no notification sent)",
                battleId);
        }

        return new BattleInitializationResult
        {
            RulesetVersion = domainRuleset.Version,
            Seed = battleSeed,
            PlayerAMaxHp = playerAMaxHp,
            PlayerBMaxHp = playerBMaxHp
        };
    }

    /// <summary>
    /// Builds the initial battle domain state from message data and player profiles.
    /// Pure function - no I/O.
    /// </summary>
    private BattleDomainState BuildInitialState(
        Guid battleId,
        Guid matchId,
        Guid playerAId,
        Guid playerBId,
        Ruleset ruleset,
        CombatProfile profileA,
        CombatProfile profileB)
    {
        var playerAStats = new PlayerStats(profileA.Strength, profileA.Stamina, profileA.Agility, profileA.Intuition);
        var playerBStats = new PlayerStats(profileB.Strength, profileB.Stamina, profileB.Agility, profileB.Intuition);

        // Compute HP using CombatMath (ONCE at battle creation)
        var derivedA = CombatMath.ComputeDerived(playerAStats, ruleset.Balance);
        var derivedB = CombatMath.ComputeDerived(playerBStats, ruleset.Balance);

        var playerA = new PlayerState(playerAId, derivedA.HpMax, playerAStats);
        var playerB = new PlayerState(playerBId, derivedB.HpMax, playerBStats);

        return new BattleDomainState(
            battleId,
            matchId,
            playerAId,
            playerBId,
            ruleset,
            BattlePhase.ArenaOpen,
            turnIndex: 0,
            noActionStreakBoth: 0,
            lastResolvedTurnIndex: 0,
            playerA,
            playerB);
    }

    /// <summary>
    /// Gets the current battle snapshot for a specific player.
    /// Validates that the player is a participant.
    /// Used by the hub for JoinBattle (reconnect/initial load).
    /// </summary>
    /// <returns>The snapshot, or null if battle not found. Throws if player is not a participant.</returns>
    public async Task<BattleSnapshot?> GetBattleSnapshotForPlayerAsync(
        Guid battleId,
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.GetStateAsync(battleId, cancellationToken);
        if (state is null)
        {
            _logger.LogWarning("Battle {BattleId} not found for user {UserId}", battleId, playerId);
            return null;
        }

        if (state.PlayerAId != playerId && state.PlayerBId != playerId)
        {
            _logger.LogWarning("User {UserId} is not a participant in battle {BattleId}", playerId, battleId);
            throw new InvalidOperationException("User is not a participant in this battle");
        }

        return state;
    }

    /// <summary>
    /// Builds the deadline for Turn 1 based on ruleset and current time.
    /// Pure function - no I/O.
    /// </summary>
    private DateTimeOffset BuildTurn1DeadlineUtc(Ruleset ruleset)
    {
        return _clock.UtcNow.AddSeconds(ruleset.TurnSeconds);
    }
}
