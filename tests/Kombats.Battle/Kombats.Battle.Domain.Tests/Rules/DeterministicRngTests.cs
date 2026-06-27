using FluentAssertions;
using Kombats.Battle.Domain.Model;
using Kombats.Battle.Domain.Rules;
using Xunit;

namespace Kombats.Battle.Domain.Tests.Rules;

public class DeterministicRngTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var rng1 = new DeterministicRandomProvider(42);
        var rng2 = new DeterministicRandomProvider(42);

        for (int i = 0; i < 100; i++)
        {
            rng1.NextDecimal(0, 100).Should().Be(rng2.NextDecimal(0, 100));
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var rng1 = new DeterministicRandomProvider(42);
        var rng2 = new DeterministicRandomProvider(43);

        var values1 = Enumerable.Range(0, 10).Select(_ => rng1.NextDecimal(0, 100)).ToList();
        var values2 = Enumerable.Range(0, 10).Select(_ => rng2.NextDecimal(0, 100)).ToList();

        values1.Should().NotEqual(values2);
    }

    [Fact]
    public void NextDecimal_ValuesInRange()
    {
        var rng = new DeterministicRandomProvider(42);

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextDecimal(10, 20);
            value.Should().BeGreaterThanOrEqualTo(10);
            value.Should().BeLessThanOrEqualTo(20);
        }
    }

    [Fact]
    public void NextDecimal_EqualMinMax_ReturnsExactValue()
    {
        var rng = new DeterministicRandomProvider(42);
        rng.NextDecimal(5, 5).Should().Be(5);
    }

    [Fact]
    public void NextDecimal_MinGreaterThanMax_Throws()
    {
        var rng = new DeterministicRandomProvider(42);
        var act = () => rng.NextDecimal(10, 5);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IntSeedConstructor_ProducesDeterministicResults()
    {
        var rng1 = new DeterministicRandomProvider(seed: 42);
        var rng2 = new DeterministicRandomProvider(seed: 42);

        rng1.NextDecimal(0, 1).Should().Be(rng2.NextDecimal(0, 1));
    }

    [Fact]
    public void ZeroSeed_ProducesValidOutput()
    {
        var rng = new DeterministicRandomProvider(0UL);
        var value = rng.NextDecimal(0, 100);
        value.Should().BeGreaterThanOrEqualTo(0);
        value.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void TurnRng_SameSeedSameState_ProducesSameResults()
    {
        var battleId = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var ruleset = TestHelpers.DefaultRuleset(42);

        var state = new BattleDomainState(
            battleId, matchId, playerAId, playerBId, ruleset,
            BattlePhase.Resolving, turnIndex: 1, noActionStreakBoth: 0,
            lastResolvedTurnIndex: 0,
            new PlayerState(playerAId, 100, new PlayerStats(10, 10, 10, 10)),
            new PlayerState(playerBId, 100, new PlayerStats(10, 10, 10, 10)));

        var (rngAtoB1, rngBtoA1) = DeterministicTurnRng.Create(state);
        var (rngAtoB2, rngBtoA2) = DeterministicTurnRng.Create(state);

        // Same state produces same RNG streams
        rngAtoB1.NextDecimal(0, 1).Should().Be(rngAtoB2.NextDecimal(0, 1));
        rngBtoA1.NextDecimal(0, 1).Should().Be(rngBtoA2.NextDecimal(0, 1));
    }

    [Fact]
    public void TurnRng_AtoBAndBtoA_AreIndependent()
    {
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var ruleset = TestHelpers.DefaultRuleset(42);

        var state = new BattleDomainState(
            battleId, Guid.NewGuid(), playerAId, playerBId, ruleset,
            BattlePhase.Resolving, 1, 0, 0,
            new PlayerState(playerAId, 100, new PlayerStats(10, 10, 10, 10)),
            new PlayerState(playerBId, 100, new PlayerStats(10, 10, 10, 10)));

        var (rngAtoB, rngBtoA) = DeterministicTurnRng.Create(state);

        // A->B and B->A should use different streams
        var aToBValues = Enumerable.Range(0, 10).Select(_ => rngAtoB.NextDecimal(0, 1000)).ToList();
        var bToAValues = Enumerable.Range(0, 10).Select(_ => rngBtoA.NextDecimal(0, 1000)).ToList();

        aToBValues.Should().NotEqual(bToAValues);
    }

    [Fact]
    public void TurnRng_DifferentTurnIndices_ProduceDifferentStreams()
    {
        var battleId = Guid.NewGuid();
        var playerAId = Guid.NewGuid();
        var playerBId = Guid.NewGuid();
        var ruleset = TestHelpers.DefaultRuleset(42);

        var state1 = new BattleDomainState(
            battleId, Guid.NewGuid(), playerAId, playerBId, ruleset,
            BattlePhase.Resolving, 1, 0, 0,
            new PlayerState(playerAId, 100, new PlayerStats(10, 10, 10, 10)),
            new PlayerState(playerBId, 100, new PlayerStats(10, 10, 10, 10)));

        var state2 = new BattleDomainState(
            battleId, Guid.NewGuid(), playerAId, playerBId, ruleset,
            BattlePhase.Resolving, 2, 0, 1,
            new PlayerState(playerAId, 100, new PlayerStats(10, 10, 10, 10)),
            new PlayerState(playerBId, 100, new PlayerStats(10, 10, 10, 10)));

        var (rng1AtoB, _) = DeterministicTurnRng.Create(state1);
        var (rng2AtoB, _) = DeterministicTurnRng.Create(state2);

        rng1AtoB.NextDecimal(0, 1000).Should().NotBe(rng2AtoB.NextDecimal(0, 1000));
    }
}
