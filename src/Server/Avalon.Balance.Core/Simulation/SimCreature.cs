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

    public required float HasteCap { get; init; }

    /// <summary>
    /// As Creature.BaseMaxHealth: the maximum health it spawned with, which a health aura is folded onto; 0 leaves its
    /// health to whoever set it. Settable so a test that gives a creature its own health can set its base too.
    /// </summary>
    public uint BaseMaxHealth { get; set; }

    /// <summary>As Creature.SwingInterval: BaseAttackTime over 1 + haste / 100, haste at most the cap.</summary>
    public float SwingInterval => Haste.Scale(Template.BaseAttackTime, MathF.Min(HastePct, HasteCap));

    /// <summary>The forest scripts' rotation in melee: the first ready special in kit order, else the basic.</summary>
    public SimAbility? Choose() =>
        Specials.FirstOrDefault(s => s.Ready && !RangedOnly.Contains(s.Id))
        ?? (Basic is { Ready: true } basic ? basic : null);

    /// <summary>
    /// As Creature.ApplyAuraStats: its attack, defence and haste with its auras folded in; its maximum health
    /// <see cref="BaseMaxHealth" /> with its health aura on top, at least 1, its health keeping its share. A corpse's
    /// health, and one with no base, is never changed.
    /// </summary>
    public void ApplyAuraStats()
    {
        AuraStatTotals totals = AuraTotals();
        Attack = AuraStats.Fold(Derived.Attacker, totals);
        Defence = AuraStats.Fold(Derived.Defence, totals);
        HastePct = AuraStats.Apply(0f, totals, AuraStat.HastePct);

        if (BaseMaxHealth == 0 || CurrentHealth == 0)
            return;

        uint max = Math.Max(1u, AuraStats.Apply(BaseMaxHealth, totals, AuraStat.MaxHealth));
        if (max == Health)
            return;

        CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, Health, max);
        Health = max;
    }

    /// <summary>
    /// A creature as CreatureSpawner builds it, with the abilities its script's kit loads (CreatureAbilities.Load):
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
        List<SimAbility> specials = kit.Specials.Distinct().Where(id => id != kit.Basic).Select(Load).OfType<SimAbility>().ToList();

        var creature = new SimCreature
        {
            Name = $"{template.Name} #{index + 1}",
            Template = template,
            Derived = derived,
            Basic = basic,
            Specials = specials,
            RangedOnly = kit.RangedOnly,
            HasteCap = data.Combat.Formula.HasteCap,
            Attack = derived.Attacker,
            Defence = derived.Defence,
            Level = derived.Level,
            Health = derived.Health,
            CurrentHealth = derived.Health,
            BaseMaxHealth = derived.Health,
            PowerType = PowerType.None,
        };
        if (basic is not null) creature.Abilities.Add(basic);
        creature.Abilities.AddRange(specials);
        return creature;
    }
}
