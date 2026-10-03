using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Abilities;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>Aura rows the catalog accepts, one per behaviour. Each test changes only what it is about.</summary>
internal static class AuraTestData
{
    public static AuraTemplate Bleed(uint id = 901) => new()
    {
        Id = new AuraId(id), Name = "Bleed", Icon = "bleed", Kind = AuraKind.Harmful, DurationMs = 12000,
        TickIntervalMs = 3000, PeriodicKind = AuraPeriodicKind.Damage, PeriodicBase = 12f, ScalingStat = ScalingStat.Attack,
        ScalingCoefficient = 0.25f, Stacking = AuraStacking.Stack, MaxStacks = 3,
    };

    public static AuraTemplate Burn(uint id = 902) => new()
    {
        Id = new AuraId(id), Name = "Burn", Icon = "burn", Kind = AuraKind.Harmful, DurationMs = 9000,
        TickIntervalMs = 3000, PeriodicKind = AuraPeriodicKind.Damage, PeriodicBase = 24f, ScalingStat = ScalingStat.Ability,
        ScalingCoefficient = 0.6f, Stacking = AuraStacking.Refresh, MaxStacks = 1,
    };

    public static AuraTemplate Crippled(uint id = 903) => new()
    {
        Id = new AuraId(id), Name = "Crippled", Icon = "crippled", Kind = AuraKind.Harmful, DurationMs = 6000,
        Stacking = AuraStacking.Refresh, MaxStacks = 1,
        Modifiers = [new AuraStatModifier { AuraId = new AuraId(id), Stat = AuraStat.MovementSpeed, Kind = AuraModifierKind.Flat, Value = -30f }],
    };

    public static AuraTemplate Renew(uint id = 904) => new()
    {
        Id = new AuraId(id), Name = "Renew", Icon = "renew", Kind = AuraKind.Helpful, DurationMs = 12000,
        TickIntervalMs = 3000, PeriodicKind = AuraPeriodicKind.Heal, PeriodicBase = 24f, ScalingStat = ScalingStat.Ability,
        ScalingCoefficient = 0.4f, Stacking = AuraStacking.Refresh, MaxStacks = 1,
    };

    public static AuraTemplate Fortified(uint id = 905) => new()
    {
        Id = new AuraId(id), Name = "Fortified", Icon = "fortified", Kind = AuraKind.Helpful, DurationMs = 30000,
        Stacking = AuraStacking.Refresh, MaxStacks = 1,
        Modifiers = [new AuraStatModifier { AuraId = new AuraId(id), Stat = AuraStat.Armor, Kind = AuraModifierKind.Percent, Value = 20f }],
    };

    /// <summary>A harmful aura each caster keeps its own copy of: 2 a tick for 6 s.</summary>
    public static AuraTemplate Independent(uint id = 906) => new()
    {
        Id = new AuraId(id), Name = "Mark", Icon = "mark", Kind = AuraKind.Harmful, DurationMs = 6000, TickIntervalMs = 3000,
        PeriodicKind = AuraPeriodicKind.Damage, PeriodicBase = 4f, Stacking = AuraStacking.Independent, MaxStacks = 1,
    };

    /// <summary><paramref name="template" /> with <paramref name="script" /> named as its ScriptName.</summary>
    public static AuraTemplate Scripted(AuraTemplate template, string script)
    {
        template.ScriptName = script;
        return template;
    }
}
