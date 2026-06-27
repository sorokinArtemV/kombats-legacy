using System.Net;
using FluentAssertions;
using Kombats.Players.Api.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Kombats.Players.Api.Tests;

public sealed class ClaimsEndpointTests : IDisposable
{
    private readonly HttpClient _devClient;
    private readonly PlayersApiFactory _devFactory;

    public ClaimsEndpointTests()
    {
        _devFactory = new PlayersApiFactory { AuthenticateRequests = true };
        _devClient = _devFactory.CreateClient();
    }

    public void Dispose()
    {
        _devClient.Dispose();
        _devFactory.Dispose();
    }

    [Fact]
    public async Task ClaimsEndpoint_InDevelopment_Returns200()
    {
        // PlayersApiFactory defaults to Development environment
        var response = await _devClient.GetAsync("/api/v1/me/claims");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClaimsEndpoint_InProduction_Returns404()
    {
        using var prodFactory = new PlayersApiFactory
        {
            AuthenticateRequests = true,
            EnvironmentName = "Production"
        };
        using var prodClient = prodFactory.CreateClient();

        var response = await prodClient.GetAsync("/api/v1/me/claims");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FakeHealthEndpoint_Returns404()
    {
        // The old /health endpoint was removed. Verify it's gone.
        var response = await _devClient.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
