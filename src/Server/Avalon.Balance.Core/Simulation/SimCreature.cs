using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;

namespace Avalon.Balance.Core;

public sealed class SimCreature : SimUnit
{
    public required CreatureTemplate Template { get; init; }

    public required DerivedCreatureStats Derived { get; init; }

    public SimAbility? Basic { get; private init; }

    public IReadOnlyList<SimAbility> Specials { get; private init; } = [];

    /// <summary>Specials this creature casts only from range, which never happens in melee.</summary>
    public IReadOnlySet<AbilityId> RangedOnly { get; private init; } = new HashSet<AbilityId>();

    public required float AttackSpeedCap { get; init; }

    public required float CastSpeedCap { get; init; }

    public required float CooldownRecoveryCap { get; init; }

    /// <summary>
    /// As Creature.AttackSpeedPct, CastSpeedPct and CooldownRecoveryPct: raw, set by its auras. Public so a test can build a
    /// creature past its cap.
    /// </summary>
    public float AttackSpeedPct { get; set; }

    public float CastSpeedPct { get; set; }

    public float CooldownRecoveryPct { get; set; }

    /// <summary>
    /// As Creature.BaseMaxHealth: the maximum health it spawned with, which a health aura is folded onto; 0 leaves its
    /// health to whoever set it. Settable so a test that gives a creature its own health can set its base too.
    /// </summary>
    public uint BaseMaxHealth { get; set; }

    /// <summary>As Creature.SwingInterval: BaseAttackTime over 1 + attack speed / 100, attack speed at most the cap.</summary>
    public float SwingInterval => Speed.Scale(Template.BaseAttackTime, MathF.Min(AttackSpeedPct, AttackSpeedCap));

    /// <summary>As Creature.Pace: each speed from 0 to its cap, and BaseAttackTime.</summary>
    public CasterPace Pace => new(CasterPace.Bound(AttackSpeedPct, AttackSpeedCap), CasterPace.Bound(CastSpeedPct, CastSpeedCap),
        CasterPace.Bound(CooldownRecoveryPct, CooldownRecoveryCap), Template.BaseAttackTime);

    /// <summary>The forest scripts' rotation in melee: the first ready special in kit order, else the basic.</summary>
    public SimAbility? Choose() =>
        Specials.FirstOrDefault(s => s.Ready && !RangedOnly.Contains(s.Id))
        ?? (Basic is { Ready: true } basic ? basic : null);

    /// <summary>
    /// As Creature.ApplyAuraStats: its attack, defence and speeds with its auras folded in; its ward's maximum its derived
    /// ward with its ward aura on top, the ward keeping its share; its maximum health <see cref="BaseMaxHealth" /> with its
    /// health aura on top, at least 1, its health keeping its share. A corpse's health, and one with no base, is never
    /// changed.
    /// </summary>
    public void ApplyAuraStats(DateTime now)
    {
        AuraStatTotals totals = AuraTotals();
        Attack = AuraStats.Fold(Derived.Attacker, totals);
        Defence = AuraStats.Fold(Derived.Defence, totals);
        AttackSpeedPct = AuraStats.Apply(0f, totals, AuraStat.AttackSpeed);
        CastSpeedPct = AuraStats.Apply(0f, totals, AuraStat.CastSpeed);
        CooldownRecoveryPct = AuraStats.Apply(0f, totals, AuraStat.CooldownRecovery);
        SetWardMax(AuraStats.Apply(Derived.Ward, totals, AuraStat.MaxWard), now);

        if (BaseMaxHealth == 0 || CurrentHealth == 0)
            return;

        uint max = Math.Max(1u, AuraStats.Apply(BaseMaxHealth, totals, AuraStat.MaxHealth));
        if (max == Health)
            return;

        CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, Health, max);
        Health = max;
    }

    /// <summary>
    /// A creature as CreatureSpawner builds it, its ward full, with the abilities its script's kit loads (CreatureAbilities.Load):
    /// an id the catalog lacks is left out, as there.
    /// </summary>
    public static SimCreature Create(BalanceData data, CreatureTemplate template, ushort level, int index)
    {
        CreatureKit kit = (template.ScriptName is not null && CreatureKits.ByScript.TryGetValue(template.ScriptName, out CreatureKit? found) ? found : null)
            ?? throw new InvalidDataException($"CreatureTemplate {template.Id.Value} '{template.Name}' has no kit");
        DerivedCreatureStats derived = data.CreatureStats.Derive(template, level);

        SimAbility? Load(AbilityId id) =>
            data.Abilities.TryGetValue(id, out AbilityTemplate? row) ? new SimAbility(row) : null;

        SimAbility? basic = Load(kit.Basic);
        var specials = kit.Specials.Distinct().Where(id => id != kit.Basic).Select(Load).OfType<SimAbility>().ToList();

        var creature = new SimCreature
        {
            Name = $"{template.Name} #{index + 1}",
            Template = template,
            Derived = derived,
            Basic = basic,
            Specials = specials,
            RangedOnly = kit.RangedOnly,
            AttackSpeedCap = data.Combat.Formula.AttackSpeedCap,
            CastSpeedCap = data.Combat.Formula.CastSpeedCap,
            CooldownRecoveryCap = data.Combat.Formula.CooldownRecoveryCap,
            Attack = derived.Attacker,
            Defence = derived.Defence,
            Level = derived.Level,
            Health = derived.Health,
            CurrentHealth = derived.Health,
            BaseMaxHealth = derived.Health,
            PowerType = PowerType.None,
        };
        creature.ResetWard(derived.Ward);
        if (basic is not null) creature.Abilities.Add(basic);
        creature.Abilities.AddRange(specials);
        return creature;
    }
}
