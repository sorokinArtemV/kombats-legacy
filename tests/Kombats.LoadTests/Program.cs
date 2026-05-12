using System.CommandLine;
using Kombats.LoadTests.Configuration;
using Kombats.LoadTests.Scenarios;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kombats.LoadTests;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Shared options (built per-command since System.CommandLine 2.0.7 binds them
        // via parseResult lookups inside SetAction).
        Option<string?> MakeOpt(string name, string description) =>
            new(name) { Description = description };

        var manifestOpt = MakeOpt("--manifest", "Path to users-manifest.json (overrides appsettings).");
        var bffUrlOpt = MakeOpt("--bff", "BFF base URL (overrides appsettings).");
        var keycloakUrlOpt = MakeOpt("--keycloak", "Keycloak base URL (overrides appsettings).");

        var smoke = new Command("smoke", "Run one bot pair via plain Task.WhenAll. Verifies the full lifecycle without NBomber.")
        {
            manifestOpt, bffUrlOpt, keycloakUrlOpt,
        };
        smoke.SetAction(async (parseResult, ct) =>
        {
            var opts = LoadOptions(parseResult.GetValue(manifestOpt), parseResult.GetValue(bffUrlOpt), parseResult.GetValue(keycloakUrlOpt));
            return await RunSmokeAsync(opts, ct);
        });

        var probe = new Command("single-bot", "One bot connects + joins queue + waits 10s + leaves. Verifies auth.")
        {
            manifestOpt, bffUrlOpt, keycloakUrlOpt,
        };
        probe.SetAction(async (parseResult, ct) =>
        {
            var opts = LoadOptions(parseResult.GetValue(manifestOpt), parseResult.GetValue(bffUrlOpt), parseResult.GetValue(keycloakUrlOpt));
            return await RunProbeAsync(opts, ct);
        });

        var pairCountOpt = new Option<int?>("--count") { Description = "Pair count (max 25 for NBomber Community license)." };
        var durationOpt = new Option<int?>("--duration") { Description = "Test duration in seconds." };
        var load = new Command("load", "NBomber load run. Default 25 pairs / 50 bots / 120s.")
        {
            pairCountOpt, durationOpt, manifestOpt, bffUrlOpt, keycloakUrlOpt,
        };
        load.SetAction(async (parseResult, ct) =>
        {
            var opts = LoadOptions(parseResult.GetValue(manifestOpt), parseResult.GetValue(bffUrlOpt), parseResult.GetValue(keycloakUrlOpt));
            var count = parseResult.GetValue(pairCountOpt);
            var dur = parseResult.GetValue(durationOpt);
            return await RunLoadAsync(opts, count, dur);
        });

        var seedCountOpt = new Option<int>("--count") { DefaultValueFactory = _ => 50, Description = "Number of loadbot-* users to ensure exist." };
        var seedPasswordOpt = new Option<string>("--password") { DefaultValueFactory = _ => "loadtest", Description = "Password assigned to every load-bot user." };
        var seedUsers = new Command("seed-users", "Bulk-create loadbot-* users in Keycloak (pass-through to SeedUsers sub-project).")
        {
            seedCountOpt, seedPasswordOpt, keycloakUrlOpt,
        };
        seedUsers.SetAction(async (parseResult, ct) =>
        {
            var count = parseResult.GetRequiredValue(seedCountOpt);
            var password = parseResult.GetRequiredValue(seedPasswordOpt);
            var kc = parseResult.GetValue(keycloakUrlOpt);
            return await RunSeedAsync(count, password, kc, ct);
        });

        var root = new RootCommand("Kombats load-test harness.")
        {
            smoke, probe, load, seedUsers,
        };

        return await root.Parse(args).InvokeAsync();
    }

    private static LoadTestOptions LoadOptions(string? manifestOverride, string? bffOverride, string? kcOverride)
    {
        var baseDir = AppContext.BaseDirectory;
        var config = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build();

        var opts = new LoadTestOptions();
        config.Bind(opts);

        if (!string.IsNullOrWhiteSpace(manifestOverride)) opts.Users.ManifestPath = manifestOverride;
        if (!string.IsNullOrWhiteSpace(bffOverride)) opts.Target.BffBaseUrl = bffOverride;
        if (!string.IsNullOrWhiteSpace(kcOverride)) opts.Target.KeycloakBaseUrl = kcOverride;
        return opts;
    }

    private static ILoggerFactory MakeLoggerFactory() =>
        LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss.fff ";
            });
        });

    private static string ResolveManifestPath(LoadTestOptions opts)
    {
        var configured = opts.Users.ManifestPath;
        if (Path.IsPathRooted(configured) && File.Exists(configured)) return configured;
        foreach (var candidate in new[]
                 {
                     configured,
                     Path.Combine(AppContext.BaseDirectory, configured),
                     Path.Combine(Environment.CurrentDirectory, configured),
                 })
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var projectCandidate = Path.Combine(projectDir, configured);
        if (File.Exists(projectCandidate)) return projectCandidate;
        return configured;
    }

    private static async Task<int> RunSmokeAsync(LoadTestOptions opts, CancellationToken ct)
    {
        var users = UserPool.LoadFromFile(ResolveManifestPath(opts));
        using var lf = MakeLoggerFactory();
        return await SingleBattleScenario.RunAsync(opts, users, lf, ct);
    }

    private static async Task<int> RunProbeAsync(LoadTestOptions opts, CancellationToken ct)
    {
        var users = UserPool.LoadFromFile(ResolveManifestPath(opts));
        using var lf = MakeLoggerFactory();
        return await SingleBotProbe.RunAsync(opts, users, lf, ct);
    }

    private static async Task<int> RunLoadAsync(LoadTestOptions opts, int? count, int? duration)
    {
        var users = UserPool.LoadFromFile(ResolveManifestPath(opts));
        using var lf = MakeLoggerFactory();
        return await ConcurrentBattlesScenario.RunAsync(opts, users, lf, count, duration);
    }

    private static async Task<int> RunSeedAsync(int count, string password, string? kc, CancellationToken ct)
    {
        var keycloak = kc ?? "http://localhost:8080";
        var seedProject = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "SeedUsers");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                "run", "--project", seedProject, "--",
                "--count", count.ToString(),
                "--password", password,
                "--keycloak", keycloak,
            },
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        var p = System.Diagnostics.Process.Start(psi)!;
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
