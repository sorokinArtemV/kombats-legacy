using FluentAssertions;
using Kombats.Chat.Application.Ports;
using Kombats.Chat.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Services;

public sealed class EligibilityCheckerTests
{
    private readonly IPlayerInfoCache _cache = Substitute.For<IPlayerInfoCache>();
    private readonly IHttpClientFactory _httpClientFactory = Substitute.For<IHttpClientFactory>();
    private readonly EligibilityChecker _checker;

    public EligibilityCheckerTests()
    {
        _checker = new EligibilityChecker(_cache, _httpClientFactory, NullLogger<EligibilityChecker>.Instance);
    }

    [Fact]
    public async Task Check_CacheHit_IsReady_ReturnsEligible()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CachedPlayerInfo("Alice", "Ready"));

        var result = await _checker.CheckEligibilityAsync(id, CancellationToken.None);

        result.Eligible.Should().BeTrue();
        result.DisplayName.Should().Be("Alice");
    }

    [Fact]
    public async Task Check_CacheHit_NotReady_ReturnsNotEligible()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(new CachedPlayerInfo("Bob", "PickingName"));

        var result = await _checker.CheckEligibilityAsync(id, CancellationToken.None);

        result.Eligible.Should().BeFalse();
        result.DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task Check_CacheMiss_HttpSuccess_Ready_ReturnsEligible()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns((CachedPlayerInfo?)null);

        var handler = new FakeHandler("""{"displayName":"Charlie","onboardingState":"Ready","level":5}""");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        _httpClientFactory.CreateClient("Players").Returns(httpClient);

        var result = await _checker.CheckEligibilityAsync(id, CancellationToken.None);

        result.Eligible.Should().BeTrue();
        result.DisplayName.Should().Be("Charlie");
    }

    [Fact]
    public async Task Check_CacheMiss_HttpSuccess_NotReady_ReturnsNotEligible()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns((CachedPlayerInfo?)null);

        var handler = new FakeHandler("""{"displayName":"Dave","onboardingState":"PickingName","level":1}""");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        _httpClientFactory.CreateClient("Players").Returns(httpClient);

        var result = await _checker.CheckEligibilityAsync(id, CancellationToken.None);

        result.Eligible.Should().BeFalse();
    }

    [Fact]
    public async Task Check_CacheMiss_HttpFailure_ReturnsNotEligible()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns((CachedPlayerInfo?)null);

        var handler = new FailingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        _httpClientFactory.CreateClient("Players").Returns(httpClient);

        var result = await _checker.CheckEligibilityAsync(id, CancellationToken.None);

        result.Eligible.Should().BeFalse();
        result.DisplayName.Should().BeNull();
    }

    [Fact]
    public async Task Check_CacheMiss_HttpSuccess_PopulatesCache()
    {
        var id = Guid.NewGuid();
        _cache.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns((CachedPlayerInfo?)null);

        var handler = new FakeHandler("""{"displayName":"Eve","onboardingState":"Ready","level":3}""");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        _httpClientFactory.CreateClient("Players").Returns(httpClient);

        await _checker.CheckEligibilityAsync(id, CancellationToken.None);

        await _cache.Received(1).SetAsync(id,
            Arg.Is<CachedPlayerInfo>(c => c.Name == "Eve" && c.OnboardingState == "Ready"),
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
