using System.Text.Json;
using FluentAssertions;
using Kombats.Players.Contracts;
using Xunit;

namespace Kombats.Players.Infrastructure.Tests;

/// <summary>
/// Round-trip serialization/deserialization test for PlayerCombatProfileChanged
/// integration event contract (AD-06 compliance).
/// </summary>
public sealed class ContractSerializationTests
{
    [Fact]
    public void PlayerCombatProfileChanged_RoundTrip_PreservesAllFields()
    {
        var original = new PlayerCombatProfileChanged
        {
            MessageId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"),
            IdentityId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f12345678901"),
            CharacterId = Guid.Parse("c3d4e5f6-a7b8-9012-cdef-123456789012"),
            Name = "TestHero",
            Level = 5,
            Strength = 10,
            Agility = 8,
            Intuition = 6,
            Vitality = 12,
            IsReady = true,
            Revision = 7,
            OccurredAt = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero),
            Version = 1
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json);

        deserialized.Should().NotBeNull();
        deserialized!.MessageId.Should().Be(original.MessageId);
        deserialized.IdentityId.Should().Be(original.IdentityId);
        deserialized.CharacterId.Should().Be(original.CharacterId);
        deserialized.Name.Should().Be(original.Name);
        deserialized.Level.Should().Be(original.Level);
        deserialized.Strength.Should().Be(original.Strength);
        deserialized.Agility.Should().Be(original.Agility);
        deserialized.Intuition.Should().Be(original.Intuition);
        deserialized.Vitality.Should().Be(original.Vitality);
        deserialized.IsReady.Should().Be(original.IsReady);
        deserialized.Revision.Should().Be(original.Revision);
        deserialized.OccurredAt.Should().Be(original.OccurredAt);
        deserialized.Version.Should().Be(original.Version);
    }

    [Fact]
    public void PlayerCombatProfileChanged_RoundTrip_NullName()
    {
        var original = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = null,
            Level = 0,
            Strength = 3,
            Agility = 3,
            Intuition = 3,
            Vitality = 3,
            IsReady = false,
            Revision = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1
        };

        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().BeNull();
        deserialized.IsReady.Should().BeFalse();
        deserialized.Version.Should().Be(1);
    }

    [Fact]
    public void PlayerCombatProfileChanged_DefaultVersion_IsOne()
    {
        var evt = new PlayerCombatProfileChanged();
        evt.Version.Should().Be(1);
    }
}
