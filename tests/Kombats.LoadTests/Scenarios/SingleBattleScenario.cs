using Kombats.LoadTests.Authentication;
using Kombats.LoadTests.Configuration;
using Kombats.LoadTests.VirtualPlayer;
using Microsoft.Extensions.Logging;

namespace Kombats.LoadTests.Scenarios;

/// <summary>
/// One pair, plain Task.WhenAll, no NBomber. Used by `dotnet run -- smoke`.
/// Verifies the full lifecycle of two bots fighting end-to-end.
/// </summary>
internal static class SingleBattleScenario
{
    public static async Task<int> RunAsync(LoadTestOptions opts, UserPool users, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (users.Count < 2)
        {
            Console.Error.WriteLine($"Need at least 2 seeded users (have {users.Count}). Run seed-users --count 50 first.");
            return 2;
        }

        var logger = loggerFactory.CreateLogger("smoke");
        var tokens = new KeycloakTokenClient(opts.Target, loggerFactory.CreateLogger<KeycloakTokenClient>());

        var behavior = new RandomPlayerBehavior();
        await using var p1 = new VirtualPlayer.VirtualPlayer(
            new VirtualPlayerOptions(users[0], opts.Target, opts.Load, behavior, RandomSeed: 1),
            tokens,
            loggerFactory.CreateLogger<VirtualPlayer.VirtualPlayer>());
        await using var p2 = new VirtualPlayer.VirtualPlayer(
            new VirtualPlayerOptions(users[1], opts.Target, opts.Load, behavior, RandomSeed: 2),
            tokens,
            loggerFactory.CreateLogger<VirtualPlayer.VirtualPlayer>());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t1 = p1.RunOneBattleAsync(ct);
        var t2 = p2.RunOneBattleAsync(ct);
        var results = await Task.WhenAll(t1, t2);
        sw.Stop();

        var r1 = results[0];
        var r2 = results[1];

        logger.LogInformation(
            "[smoke] wall clock: {Elapsed}s | r1={U1}/{O1} turns={T1} battle={B1} | r2={U2}/{O2} turns={T2} battle={B2}",
            sw.Elapsed.TotalSeconds.ToString("F2"),
            r1.Username, r1.Outcome, r1.TurnsPlayed, r1.BattleId,
            r2.Username, r2.Outcome, r2.TurnsPlayed, r2.BattleId);

        PrintResult(r1);
        PrintResult(r2);

        bool ok = r1.BattleId is not null
                  && r1.BattleId == r2.BattleId
                  && (r1.Outcome is BattleOutcome.Won or BattleOutcome.Lost or BattleOutcome.Draw)
                  && (r2.Outcome is BattleOutcome.Won or BattleOutcome.Lost or BattleOutcome.Draw)
                  && r1.TurnsPlayed > 0
                  && r2.TurnsPlayed > 0;
        return ok ? 0 : 1;
    }

    private static void PrintResult(VirtualPlayerResult r)
    {
        Console.WriteLine($"---- {r.Username} ----");
        Console.WriteLine($"  outcome     : {r.Outcome}");
        Console.WriteLine($"  battleId    : {r.BattleId}");
        Console.WriteLine($"  turnsPlayed : {r.TurnsPlayed}");
        Console.WriteLine($"  auth        : {r.AuthDuration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  onboard     : {r.OnboardDuration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  connect     : {r.ConnectDuration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  queueWait   : {r.QueueWait.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  joinBattle  : {r.JoinBattleDuration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  battle      : {r.BattleDuration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  total       : {r.TotalDuration.TotalMilliseconds:F0} ms");
        Console.WriteLine($"  events      : turnOpened={r.Events.TurnOpenedCount} resolved={r.Events.TurnResolvedCount} damaged={r.Events.PlayerDamagedCount} stateUpd={r.Events.BattleStateUpdatedCount} feed={r.Events.BattleFeedUpdatedCount}");
        if (r.ErrorMessage is not null) Console.WriteLine($"  error       : {r.ErrorMessage}");
    }
}
