using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kombats.LoadTests.Configuration;

/// <summary>
/// Manifest of seeded users. Populated by the SeedUsers sub-project.
/// </summary>
internal sealed record UserCredentials(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("sub")] string Sub);

internal sealed class UserPool
{
    private readonly IReadOnlyList<UserCredentials> _users;
    private int _cursor = -1;

    public UserPool(IReadOnlyList<UserCredentials> users)
    {
        _users = users;
    }

    public int Count => _users.Count;

    /// <summary>
    /// Returns the next user from the pool, round-robin. Thread-safe.
    /// </summary>
    public UserCredentials Next()
    {
        var idx = Interlocked.Increment(ref _cursor);
        return _users[idx % _users.Count];
    }

    public UserCredentials this[int i] => _users[i];

    public static UserPool LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Users manifest not found at '{Path.GetFullPath(path)}'. " +
                "Run `dotnet run --project tests/Kombats.LoadTests/SeedUsers -- --count <N>` first.",
                path);
        }
        var json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<UserCredentials>>(json)
                   ?? throw new InvalidOperationException($"Manifest at '{path}' deserialized as null.");
        if (list.Count == 0)
        {
            throw new InvalidOperationException($"Manifest at '{path}' is empty.");
        }
        return new UserPool(list);
    }
}
