namespace Kombats.LoadTests.Configuration;

internal sealed class LoadTestOptions
{
    public TargetOptions Target { get; set; } = new();
    public LoadOptions Load { get; set; } = new();
    public ReportingOptions Reporting { get; set; } = new();
    public UsersOptions Users { get; set; } = new();
}

internal sealed class TargetOptions
{
    public string BffBaseUrl { get; set; } = "http://localhost:5000";
    public string BattleHubPath { get; set; } = "/battlehub";
    public string KeycloakBaseUrl { get; set; } = "http://localhost:8080";
    public string Realm { get; set; } = "kombats";
    public string ClientId { get; set; } = "kombats-loadtest";
    public string ClientSecret { get; set; } = "loadtest-secret-do-not-use-in-prod";
}

internal sealed class LoadOptions
{
    public int PairCount { get; set; } = 25;
    public int RampUpSeconds { get; set; } = 30;
    public int TestDurationSeconds { get; set; } = 120;
    public int QueueTimeoutSeconds { get; set; } = 60;
    public int PerBotTimeoutSeconds { get; set; } = 120;
    public int JoinBattleRetries { get; set; } = 8;
    public int JoinBattleInitialDelayMs { get; set; } = 100;
}

internal sealed class ReportingOptions
{
    public string OutputDirectory { get; set; } = "reports";
}

internal sealed class UsersOptions
{
    public string ManifestPath { get; set; } = "users-manifest.json";
}
