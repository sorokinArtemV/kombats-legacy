using Microsoft.Extensions.Configuration;

namespace Kombats.Messaging.DependencyInjection;

public class MessagingBuilder
{
    private readonly Dictionary<Type, string> _logicalKeyMap = new();

    internal MessagingBuilder() { }

    /// <summary>
    /// Registers a logical key for a message type. The logical key is used to look up
    /// the actual entity name from configuration (Messaging:Topology:EntityNameMappings).
    /// </summary>
    /// <typeparam name="T">Message type (command or event)</typeparam>
    /// <param name="logicalKey">Logical key (e.g., "CreateBattle", "BattleCreated")</param>
    public void Map<T>(string logicalKey) where T : class
    {
        _logicalKeyMap[typeof(T)] = logicalKey;
    }

    /// <summary>
    /// Directly maps a message type to an entity name (bypasses configuration lookup).
    /// Use this when you want to specify the full entity name directly (e.g., "battle.create-battle").
    /// </summary>
    /// <typeparam name="T">Message type</typeparam>
    /// <param name="entityName">Entity name (e.g., "battle.create-battle")</param>
    public MessagingBuilder MapEntityName<T>(string entityName) where T : class
    {
        _logicalKeyMap[typeof(T)] = entityName;
        return this;
    }

    /// <summary>
    /// Builds the entity name map by resolving logical keys from configuration.
    /// Keys containing a dot are treated as direct entity names (bypass config lookup).
    /// </summary>
    internal Dictionary<Type, string> BuildEntityNameMap(IConfiguration configuration)
    {
        var entityNameMap = new Dictionary<Type, string>();
        var mappingsSection = configuration.GetSection("Messaging:Topology:EntityNameMappings");
        var mappings = mappingsSection.Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();

        foreach (var (messageType, logicalKeyOrEntityName) in _logicalKeyMap)
        {
            if (logicalKeyOrEntityName.Contains('.'))
            {
                entityNameMap[messageType] = logicalKeyOrEntityName;
            }
            else
            {
                if (!mappings.TryGetValue(logicalKeyOrEntityName, out var entityName))
                {
                    throw new InvalidOperationException(
                        $"Entity name mapping not found for logical key '{logicalKeyOrEntityName}' (message type: {messageType.Name}). " +
                        $"Add it to configuration section 'Messaging:Topology:EntityNameMappings'.");
                }

                if (string.IsNullOrWhiteSpace(entityName))
                {
                    throw new InvalidOperationException(
                        $"Entity name mapping for logical key '{logicalKeyOrEntityName}' (message type: {messageType.Name}) is empty. " +
                        $"Check configuration section 'Messaging:Topology:EntityNameMappings'.");
                }

                entityNameMap[messageType] = entityName;
            }
        }

        return entityNameMap;
    }
}
