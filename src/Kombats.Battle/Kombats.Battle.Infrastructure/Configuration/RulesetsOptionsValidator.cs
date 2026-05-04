using Kombats.Battle.Infrastructure.Rules;

namespace Kombats.Battle.Infrastructure.Configuration;

internal static class RulesetsOptionsValidator
{
    public static bool Validate(BattleRulesetsOptions options)
    {
        if (options.CurrentVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:CurrentVersion must be greater than 0. Current value: {options.CurrentVersion}");
        }

        if (!options.Versions.TryGetValue(options.CurrentVersion.ToString(), out var currentVersionConfig))
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:CurrentVersion {options.CurrentVersion} not found in Battle:Rulesets:Versions. Available versions: {string.Join(", ", options.Versions.Keys)}");
        }

        if (currentVersionConfig.TurnSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:Versions:{options.CurrentVersion}:TurnSeconds must be greater than 0. Current value: {currentVersionConfig.TurnSeconds}");
        }

        if (currentVersionConfig.NoActionLimit <= 0)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:Versions:{options.CurrentVersion}:NoActionLimit must be greater than 0. Current value: {currentVersionConfig.NoActionLimit}");
        }

        if (currentVersionConfig.CombatBalance == null)
        {
            throw new InvalidOperationException(
                $"Battle:Rulesets:Versions:{options.CurrentVersion}:CombatBalance is required but is null.");
        }

        return true;
    }
}
