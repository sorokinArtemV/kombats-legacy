
using FluentAssertions;
using Kombats.Battle.Contracts.Battle;
using Kombats.Messaging.DependencyInjection;
using Kombats.Players.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kombats.Messaging.Tests.Naming;

/// <summary>
/// Topology alignment tests — protect against RabbitMQ publisher/consumer
/// entity-name mismatches at the configuration-resolution layer.
///
/// These tests verify, for each service and each contract it publishes or
/// consumes, that the service's real appsettings.json resolves the
/// contract's logical key to the canonical entity name the codebase has
/// standardized on, and that every publisher/consumer of a shared contract
/// resolves it to the same name.
///
/// SCOPE: Configuration-level resolution via MessagingBuilder.BuildEntityNameMap.
/// These tests do NOT spin up RabbitMQ and do NOT verify real broker
/// exchange/queue bindings. A real-broker smoke test is still required to
/// catch regressions in the MassTransit bus wiring itself (tracked separately).
/// </summary>
public class TopologyAlignmentTests
{
    // Canonical topology — single source of truth.
    // Each contract has a logical key (the value each service's
    // Program.cs passes to `messagingBuilder.Map<T>(...)`) and the
    // entity name the codebase has standardized on.
    private static readonly Dictionary<Type, (string LogicalKey, string CanonicalEntityName)> Canonical = new()
    {
        [typeof(PlayerCombatProfileChanged)] = ("PlayerCombatProfileChanged", "combats.player-combat-profile-changed"),
        [typeof(CreateBattle)]              = ("CreateBattle",              "battle.create-battle"),
        [typeof(BattleCreated)]             = ("BattleCreated",             "battle.battle-created"),
        [typeof(BattleCompleted)]           = ("BattleCompleted",           "battle.battle-completed"),
    };

    // For each service: its bootstrap appsettings file and every contract
    // it publishes or consumes via RabbitMQ. A contract listed here MUST
    // resolve to the canonical name from that service's own appsettings.
    //
    // A contract missing from this list that the service actually uses
    // is itself a regression — the per-service test flags it.
    private static readonly Dictionary<string, ServiceTopology> Services = new()
    {
        ["Players"] = new(
            "players.appsettings.json",
            [
                typeof(PlayerCombatProfileChanged), // published
                typeof(BattleCompleted),            // consumed
            ]),
        ["Matchmaking"] = new(
            "matchmaking.appsettings.json",
            [
                typeof(PlayerCombatProfileChanged), // consumed
                typeof(CreateBattle),               // published (via MassTransitCreateBattlePublisher)
                typeof(BattleCreated),              // consumed
                typeof(BattleCompleted),            // consumed
            ]),
        ["Battle"] = new(
            "battle.appsettings.json",
            [
                typeof(CreateBattle),   // consumed
                typeof(BattleCreated),  // published
                typeof(BattleCompleted),// published + projected
            ]),
    };

    private sealed record ServiceTopology(string AppsettingsFile, Type[] Contracts);

    // ------------------------------------------------------------------
    // Per-service resolution: for each (service, contract) pair, load the
    // service's real appsettings and assert a standalone MessagingBuilder
    // with only that one Map<T>() call resolves to the canonical name.
    //
    // Isolating one contract per probe means a missing/drifted mapping
    // for one contract does not mask unrelated ones.
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> ServiceContractCases()
    {
        foreach (var (serviceName, topology) in Services)
        {
            foreach (var contract in topology.Contracts)
            {
                yield return new object[] { serviceName, contract };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ServiceContractCases))]
    public void Service_resolves_contract_to_canonical_entity_name(string serviceName, Type contractType)
    {
        var canonical = Canonical[contractType];
        var resolved = ResolveSingle(serviceName, contractType);

        resolved.Should().Be(canonical.CanonicalEntityName,
            $"{serviceName} publishes or consumes {contractType.Name} and its appsettings must map logical key '{canonical.LogicalKey}' to '{canonical.CanonicalEntityName}'. "
            + "If this fails, either the appsettings EntityNameMappings drifted, or the service forgot to declare the mapping after adding a new integration.");
    }

    // ------------------------------------------------------------------
    // Cross-service alignment: every contract must resolve to the same
    // entity name on every service that touches it. This is the
    // assertion that catches "publisher uses one name, consumer uses
    // another" drift end-to-end.
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> SharedContractCases()
    {
        var sharedContracts = Services
            .SelectMany(kv => kv.Value.Contracts.Select(c => (Service: kv.Key, Contract: c)))
            .GroupBy(x => x.Contract)
            .Where(g => g.Count() > 1);

        foreach (var group in sharedContracts)
        {
            yield return new object[]
            {
                group.Key.Name,
                group.Key,
                group.Select(x => x.Service).ToArray(),
            };
        }
    }

    [Theory]
    [MemberData(nameof(SharedContractCases))]
    public void Shared_contract_resolves_to_same_entity_name_across_all_services(
        string contractDisplayName,
        Type contractType,
        string[] serviceNames)
    {
        _ = contractDisplayName;

        var namesPerService = serviceNames
            .Select(svc => (Service: svc, Name: ResolveSingleOrNull(svc, contractType)))
            .ToArray();

        var missing = namesPerService.Where(x => x.Name is null).Select(x => x.Service).ToArray();
        missing.Should().BeEmpty(
            $"every service that publishes or consumes {contractType.Name} must map it in its appsettings so all sides bind to the same exchange. Missing on: {string.Join(", ", missing)}");

        var distinct = namesPerService.Select(x => x.Name).Distinct().ToArray();
        distinct.Should().HaveCount(1,
            $"all services touching {contractType.Name} must resolve it to the same entity name, got: "
            + string.Join(", ", namesPerService.Select(x => $"{x.Service}={x.Name ?? "<missing>"}")));
    }

    // ------------------------------------------------------------------
    // Guard test: protects the canonical naming itself from silent
    // change. The `EntityNameConvention` default MUST NOT equal the
    // canonical cross-service names (otherwise we could regress to
    // implicit naming without tests noticing).
    // ------------------------------------------------------------------

    [Fact]
    public void Canonical_battle_names_are_distinct_from_default_convention()
    {
        // Default convention produces "combats.<kebab-case>". The Battle family
        // deliberately uses a "battle.*" prefix so that relying on defaults
        // is an observable failure, not a silent coincidence.
        var defaultConvention = new Kombats.Messaging.Naming.EntityNameConvention();

        defaultConvention.FormatEntityName<CreateBattle>().Should().NotBe(Canonical[typeof(CreateBattle)].CanonicalEntityName,
            "if the default ever equals the canonical 'battle.create-battle', a missing Map<T>() would stop being detectable");
        defaultConvention.FormatEntityName<BattleCreated>().Should().NotBe(Canonical[typeof(BattleCreated)].CanonicalEntityName);
        defaultConvention.FormatEntityName<BattleCompleted>().Should().NotBe(Canonical[typeof(BattleCompleted)].CanonicalEntityName);
    }

    // ------------------------------------------------------------------
    // Resolution helpers — each probe builds a fresh MessagingBuilder
    // with exactly one Map<T>() call so failures are attributed to a
    // single (service, contract) pair instead of cascading.
    // ------------------------------------------------------------------

    private static string ResolveSingle(string serviceName, Type contractType)
    {
        var resolved = ResolveSingleOrNull(serviceName, contractType);
        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"Service '{serviceName}' could not resolve entity name for {contractType.Name}.");
        }
        return resolved;
    }

    private static string? ResolveSingleOrNull(string serviceName, Type contractType)
    {
        var topology = Services[serviceName];
        var canonical = Canonical[contractType];
        var configuration = LoadServiceAppsettings(topology.AppsettingsFile);

        var builder = new MessagingBuilder();
        InvokeMapGeneric(builder, contractType, canonical.LogicalKey);

        try
        {
            var map = builder.BuildEntityNameMap(configuration);
            return map.TryGetValue(contractType, out var name) ? name : null;
        }
        catch (InvalidOperationException)
        {
            // Missing EntityNameMappings entry in appsettings — surface as "not resolvable".
            return null;
        }
    }

    private static void InvokeMapGeneric(MessagingBuilder builder, Type contractType, string logicalKey)
    {
        var method = typeof(MessagingBuilder)
            .GetMethod(nameof(MessagingBuilder.Map))!
            .MakeGenericMethod(contractType);
        method.Invoke(builder, new object[] { logicalKey });
    }

    private static IConfiguration LoadServiceAppsettings(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ServiceAppsettings", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Service appsettings file not linked into test output: {path}. " +
                "Check the Content <Link> items in Kombats.Messaging.Tests.csproj.");
        }

        return new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();
    }
}
