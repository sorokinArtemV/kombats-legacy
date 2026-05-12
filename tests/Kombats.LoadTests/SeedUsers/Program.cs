using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kombats.LoadTests.SeedUsers;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var countOpt = new Option<int>("--count") { DefaultValueFactory = _ => 50, Description = "Number of loadbot-* users to ensure." };
        var passwordOpt = new Option<string>("--password") { DefaultValueFactory = _ => "loadtest", Description = "Password for every load-bot user." };
        var keycloakOpt = new Option<string>("--keycloak") { DefaultValueFactory = _ => "http://localhost:8080", Description = "Keycloak base URL." };
        var realmOpt = new Option<string>("--realm") { DefaultValueFactory = _ => "kombats", Description = "Realm in which to create users." };
        var adminUserOpt = new Option<string>("--admin-user") { DefaultValueFactory = _ => "admin", Description = "Master-realm admin username." };
        var adminPwdOpt = new Option<string>("--admin-password") { DefaultValueFactory = _ => "admin", Description = "Master-realm admin password." };
        var manifestOpt = new Option<string>("--manifest") { DefaultValueFactory = _ => "users-manifest.json", Description = "Path to write the {username, password, sub} manifest." };

        var root = new RootCommand("Create N loadbot-{i} users in the kombats Keycloak realm via Admin REST API. Idempotent.")
        {
            countOpt, passwordOpt, keycloakOpt, realmOpt, adminUserOpt, adminPwdOpt, manifestOpt,
        };

        root.SetAction(async (parseResult, ct) =>
        {
            var opts = new SeedOptions(
                Count: parseResult.GetRequiredValue(countOpt),
                Password: parseResult.GetRequiredValue(passwordOpt),
                KeycloakBaseUrl: parseResult.GetRequiredValue(keycloakOpt),
                Realm: parseResult.GetRequiredValue(realmOpt),
                AdminUser: parseResult.GetRequiredValue(adminUserOpt),
                AdminPassword: parseResult.GetRequiredValue(adminPwdOpt),
                ManifestPath: parseResult.GetRequiredValue(manifestOpt));
            return await RunAsync(opts, ct);
        });

        return await root.Parse(args).InvokeAsync();
    }

    private static async Task<int> RunAsync(SeedOptions opts, CancellationToken ct)
    {
        Console.WriteLine($"[seed-users] target: {opts.KeycloakBaseUrl}/realms/{opts.Realm}");
        Console.WriteLine($"[seed-users] count : {opts.Count}");

        using var http = new HttpClient { BaseAddress = new Uri(opts.KeycloakBaseUrl) };

        var adminToken = await GetAdminTokenAsync(http, opts, ct);
        http.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);

        int created = 0;
        int existed = 0;
        int failed = 0;
        var manifest = new List<ManifestEntry>(opts.Count);

        for (int i = 1; i <= opts.Count; i++)
        {
            var username = $"loadbot-{i:0000}";
            var existingId = await FindUserIdAsync(http, opts.Realm, username, ct);
            if (existingId is not null)
            {
                manifest.Add(new ManifestEntry(username, opts.Password, existingId));
                existed++;
                continue;
            }

            try
            {
                var newId = await CreateUserAsync(http, opts.Realm, username, opts.Password, ct);
                manifest.Add(new ManifestEntry(username, opts.Password, newId));
                created++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[seed-users] FAILED {username}: {ex.Message}");
                failed++;
            }
        }

        Console.WriteLine($"[seed-users] result: created={created}, existed={existed}, failed={failed}");

        await File.WriteAllTextAsync(opts.ManifestPath, JsonSerializer.Serialize(manifest, ManifestJsonOptions), ct);
        Console.WriteLine($"[seed-users] manifest: {Path.GetFullPath(opts.ManifestPath)}  ({manifest.Count} entries)");

        return failed == 0 ? 0 : 1;
    }

    private static async Task<string> GetAdminTokenAsync(HttpClient http, SeedOptions opts, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "admin-cli",
            ["username"] = opts.AdminUser,
            ["password"] = opts.AdminPassword,
        });

        using var resp = await http.PostAsync("/realms/master/protocol/openid-connect/token", form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Admin token failed: HTTP {(int)resp.StatusCode}. Body: {body}");
        }
        var token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("access_token missing in admin token response.");
        return token;
    }

    private static async Task<string?> FindUserIdAsync(HttpClient http, string realm, string username, CancellationToken ct)
    {
        // exact=true so `loadbot-1` doesn't match `loadbot-10..19`.
        using var resp = await http.GetAsync($"/admin/realms/{realm}/users?username={Uri.EscapeDataString(username)}&exact=true&max=1", ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }
        return doc.RootElement[0].GetProperty("id").GetString();
    }

    private static async Task<string> CreateUserAsync(HttpClient http, string realm, string username, string password, CancellationToken ct)
    {
        var payload = new
        {
            username,
            enabled = true,
            emailVerified = true,
            email = $"{username}@kombats.local",
            firstName = username,
            lastName = "Loadbot",
            credentials = new[]
            {
                new { type = "password", value = password, temporary = false }
            }
        };

        using var resp = await http.PostAsJsonAsync($"/admin/realms/{realm}/users", payload, cancellationToken: ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Create failed: HTTP {(int)resp.StatusCode}. Body: {body}");
        }

        var location = resp.Headers.Location?.ToString();
        if (location is null)
        {
            var id = await FindUserIdAsync(http, realm, username, ct);
            return id ?? throw new InvalidOperationException($"Created user {username} but could not locate id.");
        }

        var lastSlash = location.LastIndexOf('/');
        return location[(lastSlash + 1)..];
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

internal sealed record SeedOptions(
    int Count,
    string Password,
    string KeycloakBaseUrl,
    string Realm,
    string AdminUser,
    string AdminPassword,
    string ManifestPath);

internal sealed record ManifestEntry(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("sub")] string Sub);
