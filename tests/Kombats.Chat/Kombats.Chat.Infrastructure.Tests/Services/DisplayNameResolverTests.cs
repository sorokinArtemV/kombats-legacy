using FluentAssertions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Services;

public sealed class DisplayNameResolverTests
{
    private readonly IPlayerInfoCache _cache = Substitute.For<IPlayerInfoCache>();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
    private readonly DisplayNameResolver _resolver;

    public DisplayNameResolverTests()
    {
        _resolver = new DisplayNameResolver(_cache, _httpClientFactory, NullLogger<DisplayNameResolver>.Instance);
    }

    [Fact]
    public async Task Resolve_CacheHit_ReturnsCachedName()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CachedPlayerInfo("Alice", "Ready"));

        string name = await _resolver.ResolveAsync(id, CancellationToken.None);

        name.Should().Be("Alice");
    }

    [Fact]
    public async Task Resolve_CacheMiss_HttpFailure_ReturnsUnknown()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns((CachedPlayerInfo?)null);

        // HTTP client throws
        var handler = new FailingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        _httpClientFactory.CreateClient("Players").Returns(httpClient);

        string name = await _resolver.ResolveAsync(id, CancellationToken.None);

        name.Should().Be("Unknown");
    }

    [Fact]
    public async Task Resolve_CacheMiss_HttpSuccess_PopulatesCache()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns((CachedPlayerInfo?)null);

        var handler = new FakeHandler("""{"displayName":"Bob","onboardingState":"Ready","level":1}""");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        _httpClientFactory.CreateClient("Players").Returns(httpClient);

        string name = await _resolver.ResolveAsync(id, CancellationToken.None);

        name.Should().Be("Bob");
        await _cache.Received(1).SetAsync(id,
            Arg.Is<CachedPlayerInfo>(c => c.Name == "Bob" && c.OnboardingState == "Ready"),
            Arg.Any<CancellationToken>());
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("connection refused");
    }

    private sealed class FakeHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
