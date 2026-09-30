using Avalon.Balance.Data;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Combat;
using Avalon.World.Creatures;

namespace Avalon.Balance.Simulation;

public sealed class SimCreature : SimUnit
{
    public required CreatureTemplate Template { get; init; }

    public required DerivedCreatureStats Derived { get; init; }

    public SimAbility? Basic { get; private init; }

    public IReadOnlyList<SimAbility> Specials { get; private init; } = [];

    public required float HasteCap { get; init; }

    /// <summary>As Creature.SwingInterval: BaseAttackTime over 1 + haste / 100, haste at most the cap.</summary>
    public float SwingInterval => Haste.Scale(Template.BaseAttackTime, MathF.Min(HastePct, HasteCap));

    /// <summary>The forest scripts' rotation in melee: the first ready special in kit order, else the basic.</summary>
    public SimAbility? Choose() =>
        Specials.FirstOrDefault(s => s.Ready && !CreatureKits.RangedOnly.Contains(s.Id))
        ?? (Basic is { Ready: true } basic ? basic : null);

    /// <summary>
    /// A creature as CreatureSpawner builds it, with the abilities its script's kit loads (CreatureAbilities.Load):
    /// an id the catalog lacks is left out, as there.
    /// </summary>
    public static SimCreature Create(BalanceData data, CreatureTemplate template, ushort level, int index)
    {
        CreatureAbilityKit kit = CreatureKits.For(template.ScriptName)
            ?? throw new InvalidDataException($"CreatureTemplate {template.Id.Value} '{template.Name}' has no kit");
        DerivedCreatureStats derived = data.CreatureStats.Derive(template, level);

        SimAbility? Load(AbilityId id) =>
            data.Abilities.TryGet(id, out AbilityTemplate? row) ? new SimAbility(row) : null;

        SimAbility? basic = Load(kit.Basic);
        List<SimAbility> specials = kit.Specials.Distinct().Where(id => id != kit.Basic).Select(Load).OfType<SimAbility>().ToList();

        var creature = new SimCreature
        {
            Name = $"{template.Name} #{index + 1}",
            Template = template,
            Derived = derived,
            Basic = basic,
            Specials = specials,
            HasteCap = data.Combat.Formula.HasteCap,
            Attack = derived.Attacker,
            Defence = derived.Defence,
            Level = derived.Level,
            Health = derived.Health,
            CurrentHealth = derived.Health,
            PowerType = PowerType.None,
        };
        if (basic is not null) creature.Abilities.Add(basic);
        creature.Abilities.AddRange(specials);
        return creature;
    }
}
