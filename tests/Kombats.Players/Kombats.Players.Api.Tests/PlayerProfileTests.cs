using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kombats.Abstractions;
using Kombats.Players.Api.Tests.Fixtures;
using Kombats.Players.Application.UseCases.GetPlayerProfile;
using Kombats.Players.Domain.Entities;
using NSubstitute;
using Xunit;

namespace Kombats.Players.Api.Tests;

public sealed class PlayerProfileTests : IClassFixture<PlayersApiFactory>, IDisposable
{
    private readonly PlayersApiFactory _factory;
    private readonly HttpClient _client;

    public PlayerProfileTests(PlayersApiFactory factory)
    {
        _factory = factory;
        _factory.AuthenticateRequests = true;
        _client = _factory.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task GetProfile_ExistingPlayer_Returns200WithProfile()
    {
        var identityId = Guid.Parse(PlayersApiFactory.TestSubjectId);
        var expected = new GetPlayerProfileQueryResponse(
            PlayerId: identityId,
            DisplayName: "TestPlayer",
            Level: 5,
            Strength: 10,
            Agility: 8,
            Intuition: 6,
            Vitality: 12,
            Wins: 3,
            Losses: 1,
            OnboardingState: OnboardingState.Ready,
            AvatarId: "default");

        _factory.GetPlayerProfileHandler
            .HandleAsync(Arg.Any<GetPlayerProfileQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(expected));

        var response = await _client.GetAsync($"/api/v1/players/{identityId}/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetPlayerProfileQueryResponse>();
        body.Should().NotBeNull();
        body!.PlayerId.Should().Be(identityId);
        body.DisplayName.Should().Be("TestPlayer");
        body.Level.Should().Be(5);
        body.Strength.Should().Be(10);
        body.Agility.Should().Be(8);
        body.Intuition.Should().Be(6);
        body.Vitality.Should().Be(12);
        body.Wins.Should().Be(3);
        body.Losses.Should().Be(1);
        body.OnboardingState.Should().Be(OnboardingState.Ready);
    }

    [Fact]
    public async Task GetProfile_NonexistentPlayer_Returns404()
    {
        var identityId = Guid.NewGuid();

        _factory.GetPlayerProfileHandler
            .HandleAsync(Arg.Any<GetPlayerProfileQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GetPlayerProfileQueryResponse>(
                Error.NotFound("GetPlayerProfile.NotFound", "Player not found.")));

        var response = await _client.GetAsync($"/api/v1/players/{identityId}/profile");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProfile_WithoutAuth_Returns401()
    {
        _factory.AuthenticateRequests = false;
        using var unauthClient = _factory.CreateClient();

        var response = await unauthClient.GetAsync($"/api/v1/players/{Guid.NewGuid()}/profile");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
