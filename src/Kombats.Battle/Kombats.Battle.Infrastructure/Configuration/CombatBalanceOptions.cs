namespace Kombats.Battle.Infrastructure.Configuration;

/// <summary>
/// Sub-option types for combat balance configuration binding.
/// Used by CombatBalanceVersionOptions in versioned ruleset configuration.
/// </summary>
public class HpBalanceOptions
{
    public int BaseHp { get; set; }
    public int HpPerEnd { get; set; }
}

public class DamageBalanceOptions
{
    public int BaseWeaponDamage { get; set; }
    public decimal DamagePerStr { get; set; }
    public decimal DamagePerAgi { get; set; }
    public decimal DamagePerInt { get; set; }
    public decimal SpreadMin { get; set; }
    public decimal SpreadMax { get; set; }
}

public class MfBalanceOptions
{
    public int MfPerAgi { get; set; }
    public int MfPerInt { get; set; }
}

public class ChanceBalanceOptions
{
    public decimal Base { get; set; }
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public decimal Scale { get; set; }
    public decimal KBase { get; set; }
}

public class CritEffectBalanceOptions
{
    public string Mode { get; set; } = "BypassBlock";
    public decimal Multiplier { get; set; }
    public decimal HybridBlockMultiplier { get; set; }
}
