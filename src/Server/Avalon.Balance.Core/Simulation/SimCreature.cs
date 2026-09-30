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

    /// <summary>As Creature.SwingInterval: BaseAttackTime over 1 + haste / 100, haste at most the cap.</summary>
    public float SwingInterval => Haste.Scale(Template.BaseAttackTime, MathF.Min(HastePct, HasteCap));

    /// <summary>The forest scripts' rotation in melee: the first ready special in kit order, else the basic.</summary>
    public SimAbility? Choose() =>
        Specials.FirstOrDefault(s => s.Ready && !RangedOnly.Contains(s.Id))
        ?? (Basic is { Ready: true } basic ? basic : null);

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
            PowerType = PowerType.None,
        };
        if (basic is not null) creature.Abilities.Add(basic);
        creature.Abilities.AddRange(specials);
        return creature;
    }
}
