using Kombats.LoadTests.Authentication;
using Kombats.LoadTests.Configuration;
using Kombats.LoadTests.Reporting;
using Kombats.LoadTests.VirtualPlayer;
using Microsoft.Extensions.Logging;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;

namespace Kombats.LoadTests.Scenarios;

/// <summary>
/// NBomber-driven scenario: N bot pairs, each pair fights one battle.
/// Capped at 25 pairs (50 bots) by the NBomber Community license.
/// </summary>
internal static class ConcurrentBattlesScenario
{
    private const int CommunityLicenseCap = 25;

    public static async Task<int> RunAsync(LoadTestOptions opts, UserPool users, ILoggerFactory loggerFactory, int? pairOverride, int? durationOverride)
    {
        int pairs = pairOverride ?? opts.Load.PairCount;
        if (pairs <= 0)
        {
            Console.Error.WriteLine("--count must be > 0");
            return 2;
        }
        if (pairs > CommunityLicenseCap)
        {
            Console.Error.WriteLine(
                $"--count {pairs} exceeds NBomber Community license cap of {CommunityLicenseCap} concurrent scenarios. " +
                "Stay at or below 25 pairs (50 bots) for the Community license. See https://nbomber.com/docs/getting-started/license/");
            return 2;
        }

        if (users.Count < pairs)
        {
            Console.Error.WriteLine($"Need at least {pairs} seeded users (have {users.Count}). Run seed-users --count {Math.Max(pairs * 2, 50)}.");
            return 2;
        }

        int totalDurationSeconds = durationOverride ?? opts.Load.TestDurationSeconds;
        int rampUpSeconds = Math.Min(opts.Load.RampUpSeconds, Math.Max(1, totalDurationSeconds - 1));
        int steadyDurationSeconds = Math.Max(1, totalDurationSeconds - rampUpSeconds);

        var logger = loggerFactory.CreateLogger("load");
        logger.LogInformation(
            "[load] keep-constant {Copies} concurrent bots, ramp {Ramp}s then hold {Steady}s (total {Total}s)",
            pairs, rampUpSeconds, steadyDurationSeconds, totalDurationSeconds);

        var tokens = new KeycloakTokenClient(opts.Target, loggerFactory.CreateLogger<KeycloakTokenClient>());
        var behavior = new RandomPlayerBehavior();
        // Monotonic counter shared across all scenario iterations. KeepConstant
        // recycles scenario instances as their previous iteration finishes —
        // each new iteration grabs the next index, modulo the seeded user pool.
        int iterationCursor = -1;

        // Per-iteration phase log lives next to the reports/ folder, not inside it:
        // NBomber's WithReportFolder wipes the target directory at the end of
        // a run (including subdirectories), which would erase our phase data.
        var iterationsDir = Path.GetFullPath(
            Path.Combine(opts.Reporting.OutputDirectory, "..", "iteration-logs"));
        Directory.CreateDirectory(iterationsDir);
        var iterationsPath = Path.Combine(
            iterationsDir,
            $"iterations-{DateTime.Now:yyyy-MM-dd--HH-mm-ss}.jsonl");
        await using var recorder = new IterationRecorder(iterationsPath);
        logger.LogInformation("[load] per-iteration phase log: {Path}", iterationsPath);

        var scenario = Scenario.Create("battle_session", async scenarioContext =>
        {
            var idx = Interlocked.Increment(ref iterationCursor);
            var user = users[idx % users.Count];

            await using var bot = new VirtualPlayer.VirtualPlayer(
                new VirtualPlayerOptions(user, opts.Target, opts.Load, behavior, RandomSeed: 42 + idx),
                tokens,
                loggerFactory.CreateLogger<VirtualPlayer.VirtualPlayer>());

            var result = await bot.RunOneBattleAsync(scenarioContext.ScenarioCancellationToken);
            recorder.Record(result);

            if (result.Outcome is BattleOutcome.Won or BattleOutcome.Lost or BattleOutcome.Draw)
            {
                return Response.Ok(
                    statusCode: result.Outcome.ToString(),
                    sizeBytes: result.TurnsPlayed,
                    message: $"battle={result.BattleId} turns={result.TurnsPlayed} battleMs={result.BattleDuration.TotalMilliseconds:F0}");
            }
            return Response.Fail(statusCode: result.Outcome.ToString(), message: result.ErrorMessage ?? result.Outcome.ToString());
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            // Close Model: keep N scenario iterations running at all times. Each
            // iteration is one full battle (queue → pair → fight → BattleEnded),
            // typically 30–60 s wall clock. As one finishes, NBomber immediately
            // starts the next so the concurrency stays at `pairs`. This is the
            // load shape we want for a stateful per-bot session — Inject would
            // spread iterations across the duration regardless of how long each
            // takes, leaving bots without queue partners.
            Simulation.RampingConstant(
                copies: pairs,
                during: TimeSpan.FromSeconds(rampUpSeconds)),
            Simulation.KeepConstant(
                copies: pairs,
                during: TimeSpan.FromSeconds(steadyDurationSeconds)))
        .WithMaxFailCount(int.MaxValue);

        Directory.CreateDirectory(opts.Reporting.OutputDirectory);

        var stats = NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFolder(opts.Reporting.OutputDirectory)
            .WithReportFormats(ReportFormat.Html, ReportFormat.Csv, ReportFormat.Md, ReportFormat.Txt)
            .Run();

        var run = stats.ScenarioStats.FirstOrDefault();
        if (run is null)
        {
            Console.Error.WriteLine("NBomber returned no scenario stats.");
            return 1;
        }

        Console.WriteLine($"[load] ok={run.Ok.Request.Count} fail={run.Fail.Request.Count}");
        Console.WriteLine($"[load] iteration log: {iterationsPath}");
        return 0;
    }
}
