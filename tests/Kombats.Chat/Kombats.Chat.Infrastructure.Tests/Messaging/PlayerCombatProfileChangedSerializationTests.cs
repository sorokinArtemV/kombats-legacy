using System.Text.Json;
using FluentAssertions;
using Kombats.Players.Contracts;
using Xunit;

namespace Kombats.Chat.Infrastructure.Tests.Messaging;

/// <summary>
/// Contract round-trip test for <see cref="PlayerCombatProfileChanged"/>.
/// The Chat service consumes this event; any serializer change in the publisher
/// (Players) or consumer (Chat) must preserve the Version field and all payload fields.
/// </summary>
public sealed class PlayerCombatProfileChangedSerializationTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields_IncludingVersion()
    {
        var original = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = "Alice",
            Level = 12,
            Strength = 15,
            Agility = 14,
            Intuition = 13,
            Vitality = 16,
            IsReady = true,
            Revision = 7,
            OccurredAt = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.Zero),
            Version = 1,
        };

        string json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json);

        roundTripped.Should().NotBeNull();
        roundTripped!.MessageId.Should().Be(original.MessageId);
        roundTripped.IdentityId.Should().Be(original.IdentityId);
        roundTripped.CharacterId.Should().Be(original.CharacterId);
        roundTripped.Name.Should().Be(original.Name);
        roundTripped.Level.Should().Be(original.Level);
        roundTripped.Strength.Should().Be(original.Strength);
        roundTripped.Agility.Should().Be(original.Agility);
        roundTripped.Intuition.Should().Be(original.Intuition);
        roundTripped.Vitality.Should().Be(original.Vitality);
        roundTripped.IsReady.Should().Be(original.IsReady);
        roundTripped.Revision.Should().Be(original.Revision);
        roundTripped.OccurredAt.Should().Be(original.OccurredAt);
        roundTripped.Version.Should().Be(original.Version);
    }

    [Fact]
    public void RoundTrip_IsReadyFalse_PreservesFlag()
    {
        var original = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = "Bob",
            IsReady = false,
            Revision = 1,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1,
        };

        string json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json);

        roundTripped!.IsReady.Should().BeFalse();
        roundTripped.Name.Should().Be("Bob");
    }

    [Fact]
    public void RoundTrip_NullName_DeserializesAsNull()
    {
        var original = new PlayerCombatProfileChanged
        {
            MessageId = Guid.NewGuid(),
            IdentityId = Guid.NewGuid(),
            CharacterId = Guid.NewGuid(),
            Name = null,
            IsReady = false,
            Revision = 0,
            OccurredAt = DateTimeOffset.UtcNow,
            Version = 1,
        };

        string json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json);

        roundTripped!.Name.Should().BeNull();
    }

    [Fact]
    public void Deserialization_MissingVersion_DefaultsToOne()
    {
        // Additive-only schema: older publishers without Version must still deserialize.
        string json = """
        {
          "MessageId": "00000000-0000-0000-0000-000000000001",
          "IdentityId": "00000000-0000-0000-0000-000000000002",
          "CharacterId": "00000000-0000-0000-0000-000000000003",
          "Name": "Legacy",
          "Level": 1,
          "Strength": 1,
          "Agility": 1,
          "Intuition": 1,
          "Vitality": 1,
          "IsReady": true,
          "Revision": 1,
          "OccurredAt": "2026-04-15T12:00:00+00:00"
        }
        """;

        var parsed = JsonSerializer.Deserialize<PlayerCombatProfileChanged>(json);

        parsed.Should().NotBeNull();
        parsed!.Version.Should().Be(1);
    }
}
