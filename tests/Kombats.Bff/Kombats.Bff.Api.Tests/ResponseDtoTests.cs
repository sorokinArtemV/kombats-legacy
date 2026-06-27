using System.Reflection;
using FluentAssertions;
using Kombats.Bff.Api.Models.Responses;
using Xunit;

namespace Kombats.Bff.Api.Tests;

public sealed class ResponseDtoTests
{
    [Fact]
    public void OnboardResponse_HasExpectedProperties()
    {
        var properties = typeof(OnboardResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] propertyNames = properties.Select(p => p.Name).ToArray();

        propertyNames.Should().Contain("CharacterId");
        propertyNames.Should().Contain("OnboardingState");
        propertyNames.Should().Contain("Name");
        propertyNames.Should().Contain("Strength");
        propertyNames.Should().Contain("Agility");
        propertyNames.Should().Contain("Intuition");
        propertyNames.Should().Contain("Vitality");
        propertyNames.Should().Contain("UnspentPoints");
        propertyNames.Should().Contain("Revision");
        propertyNames.Should().Contain("TotalXp");
        propertyNames.Should().Contain("Level");
    }

    [Fact]
    public void CharacterResponse_HasExpectedProperties()
    {
        var properties = typeof(CharacterResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] propertyNames = properties.Select(p => p.Name).ToArray();

        propertyNames.Should().Contain("CharacterId");
        propertyNames.Should().Contain("OnboardingState");
        propertyNames.Should().Contain("Name");
        propertyNames.Should().Contain("Strength");
        propertyNames.Should().Contain("UnspentPoints");
        propertyNames.Should().Contain("Revision");
        propertyNames.Should().Contain("TotalXp");
        propertyNames.Should().Contain("Level");
    }

    [Fact]
    public void CharacterResponse_OnboardingState_IsString()
    {
        var prop = typeof(CharacterResponse).GetProperty("OnboardingState");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(string),
            "BFF should expose OnboardingState as string, not enum int");
    }

    [Fact]
    public void AllocateStatsResponse_HasExpectedProperties()
    {
        var properties = typeof(AllocateStatsResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] propertyNames = properties.Select(p => p.Name).ToArray();

        propertyNames.Should().Contain("Strength");
        propertyNames.Should().Contain("Agility");
        propertyNames.Should().Contain("Intuition");
        propertyNames.Should().Contain("Vitality");
        propertyNames.Should().Contain("UnspentPoints");
        propertyNames.Should().Contain("Revision");
        propertyNames.Should().HaveCount(6, "AllocateStatsResponse should have exactly 6 properties");
    }

    [Fact]
    public void QueueStatusResponse_HasExpectedProperties()
    {
        var properties = typeof(QueueStatusResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] propertyNames = properties.Select(p => p.Name).ToArray();

        propertyNames.Should().Contain("Status");
        propertyNames.Should().Contain("MatchId");
        propertyNames.Should().Contain("BattleId");
        propertyNames.Should().Contain("MatchState");
    }

    [Fact]
    public void QueueStatusResponse_MatchId_IsNullableGuid()
    {
        var prop = typeof(QueueStatusResponse).GetProperty("MatchId");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(Guid?));
    }

    [Fact]
    public void LeaveQueueResponse_HasExpectedProperties()
    {
        var properties = typeof(LeaveQueueResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] propertyNames = properties.Select(p => p.Name).ToArray();

        propertyNames.Should().Contain("LeftQueue");
        propertyNames.Should().Contain("MatchId");
        propertyNames.Should().Contain("BattleId");
    }

    [Fact]
    public void GameStateResponse_HasExpectedProperties()
    {
        var properties = typeof(GameStateResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] propertyNames = properties.Select(p => p.Name).ToArray();

        propertyNames.Should().Contain("Character");
        propertyNames.Should().Contain("QueueStatus");
        propertyNames.Should().Contain("IsCharacterCreated");
        propertyNames.Should().Contain("DegradedServices");
    }

    [Fact]
    public void GameStateResponse_Character_IsNullable()
    {
        var prop = typeof(GameStateResponse).GetProperty("Character");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(CharacterResponse));
    }

    [Fact]
    public void GameStateResponse_QueueStatus_IsNullable()
    {
        var prop = typeof(GameStateResponse).GetProperty("QueueStatus");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(QueueStatusResponse));
    }

    [Fact]
    public void GameStateResponse_DegradedServices_IsNullableList()
    {
        var prop = typeof(GameStateResponse).GetProperty("DegradedServices");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(IReadOnlyList<string>));
    }

    [Fact]
    public void GameStateResponse_DoesNotContainLiveBattleStateFields()
    {
        // Live battle state (HP, current turn, actions, turn results) is SignalR-only (BFF-3).
        // The REST GameStateResponse must not include these fields.
        var properties = typeof(GameStateResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        string[] propertyNames = properties.Select(p => p.Name).ToArray();

        string[] forbiddenBattleStateFields =
        [
            "CurrentHp", "MaxHp", "CurrentTurn", "TurnIndex",
            "Actions", "TurnResults", "TurnResult",
            "BattleState", "LiveBattleState",
            "PlayerHp", "OpponentHp"
        ];

        foreach (string forbidden in forbiddenBattleStateFields)
        {
            propertyNames.Should().NotContain(forbidden,
                $"GameStateResponse must not contain live battle state field '{forbidden}' — live battle state is SignalR-only");
        }
    }

    [Fact]
    public void AllResponseDtos_AreRecords()
    {
        Type[] dtoTypes =
        [
            typeof(OnboardResponse),
            typeof(CharacterResponse),
            typeof(AllocateStatsResponse),
            typeof(QueueStatusResponse),
            typeof(LeaveQueueResponse),
            typeof(GameStateResponse)
        ];

        foreach (Type dtoType in dtoTypes)
        {
            // Records have a compiler-generated <Clone>$ method
            dtoType.GetMethod("<Clone>$").Should().NotBeNull(
                $"{dtoType.Name} should be a record");
        }
    }

    [Fact]
    public void AllResponseDtos_AreSealed()
    {
        Type[] dtoTypes =
        [
            typeof(OnboardResponse),
            typeof(CharacterResponse),
            typeof(AllocateStatsResponse),
            typeof(QueueStatusResponse),
            typeof(LeaveQueueResponse),
            typeof(GameStateResponse)
        ];

        foreach (Type dtoType in dtoTypes)
        {
            dtoType.IsSealed.Should().BeTrue($"{dtoType.Name} should be sealed");
        }
    }
}
