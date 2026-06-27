using Microsoft.Extensions.Options;

namespace Kombats.Chat.Infrastructure.Tests.Workers;

/// <summary>
/// Minimal in-test fake. NSubstitute cannot proxy <see cref="IOptionsMonitor{T}"/> when T
/// is an internal type (strong-named assembly + no InternalsVisibleTo to the proxy assembly).
/// </summary>
internal sealed class FakeOptionsMonitor<T> : IOptionsMonitor<T>
{
    public FakeOptionsMonitor(T current) => CurrentValue = current;

    public T CurrentValue { get; }
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
