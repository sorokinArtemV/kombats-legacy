using Kombats.LoadTests.Authentication;
using Kombats.LoadTests.Configuration;
using Kombats.LoadTests.SignalR;
using Kombats.LoadTests.Transport;
using Microsoft.Extensions.Logging;

namespace Kombats.LoadTests.Scenarios;

/// <summary>
/// One bot acquires a JWT, hits BFF onboarding, opens a SignalR connection,
/// joins the queue, waits ~10s, leaves cleanly. No battle is fought.
///
/// Smoke test for the auth + transport plumbing — if this passes, the
/// load-test stack is wired correctly.
/// </summary>
internal static class SingleBotProbe
{
    public static async Task<int> RunAsync(LoadTestOptions opts, UserPool users, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (users.Count < 1)
        {
            Console.Error.WriteLine("No seeded users found. Run seed-users first.");
            return 2;
        }

        var logger = loggerFactory.CreateLogger("probe");
        var user = users[0];
        logger.LogInformation("[probe] target: {Url}, user: {User}", opts.Target.BffBaseUrl, user.Username);

        var tokens = new KeycloakTokenClient(opts.Target, loggerFactory.CreateLogger<KeycloakTokenClient>());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var token = await tokens.GetAccessTokenAsync(user, ct);
        logger.LogInformation("[probe] token acquired in {Ms} ms (head: {Head}...)", sw.ElapsedMilliseconds, token[..Math.Min(20, token.Length)]);

        using var bff = new BffHttpClient(
            opts.Target.BffBaseUrl,
            ct2 => tokens.GetAccessTokenAsync(user, ct2),
            logger);

        sw.Restart();
        var onboard = await bff.OnboardAsync(ct);
        logger.LogInformation("[probe] onboard in {Ms} ms — state={State} revision={Rev}",
            sw.ElapsedMilliseconds, onboard?.OnboardingState, onboard?.Revision);

        if (string.Equals(onboard?.OnboardingState, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            await bff.SetNameAsync(user.Username, ct);
            logger.LogInformation("[probe] set-name done");
        }

        var state = await bff.GetGameStateAsync(ct);
        var current = state?.Character;
        if (current is not null && !string.Equals(current.OnboardingState, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            await bff.AllocateStatsAsync(strength: 1, agility: 1, intuition: 1, vitality: 0, expectedRevision: current.Revision, ct);
            logger.LogInformation("[probe] allocate-stats done");
        }

        await using var hub = new BattleHubClient(
            opts.Target.BffBaseUrl,
            opts.Target.BattleHubPath,
            ct2 => tokens.GetAccessTokenAsync(user, ct2),
            logger);

        sw.Restart();
        await hub.ConnectAsync(ct);
        logger.LogInformation("[probe] SignalR connected in {Ms} ms", sw.ElapsedMilliseconds);

        var connectionRef = $"probe-{Guid.NewGuid():N}";
        var join = await bff.JoinQueueAsync(connectionRef, ct);
        logger.LogInformation("[probe] join-queue result: status={Status} matchId={MatchId} battleId={BattleId}",
            join.Status, join.MatchId, join.BattleId);

        logger.LogInformation("[probe] waiting 10s before leaving...");
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        await bff.LeaveQueueAsync(connectionRef, ct);
        logger.LogInformation("[probe] leave-queue done");

        return 0;
    }
}
