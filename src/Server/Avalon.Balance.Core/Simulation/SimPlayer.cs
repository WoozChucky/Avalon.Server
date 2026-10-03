using Avalon.Combat;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Core;

public sealed class SimPlayer : SimUnit
{
    public required CharacterClass Class { get; init; }

    /// <summary>Its derived stats, its auras folded in (set again whenever an aura with modifiers changes).</summary>
    public required DerivedCharacterStats Stats { get; set; }

    public required uint RegenStat { get; set; }

    /// <summary>The templates it wears, which every stats refresh reads again.</summary>
    public IReadOnlyList<ItemTemplate> Worn { get; private init; } = [];

    public SimAbility Ability(uint id) =>
        Abilities.FirstOrDefault(a => a.Id == id)
        ?? throw new InvalidOperationException($"{Class} does not hold ability {id}");

    /// <summary>As CharacterEntity.GainPower, through the same rule (PowerPool.Gain).</summary>
    public void GainPower(uint amount)
    {
        uint current = CurrentPower ?? 0;
        uint next = PowerPool.Gain(PowerType, IsDead, current, Power ?? 0, amount);
        if (next != current) CurrentPower = next;
    }

    /// <summary>
    /// As the server's stats refresh after an aura change (CharacterStatsRefresh with KeepShare): the stats again with
    /// the auras folded in, each Mana or Energy pool and health keeping its share, Fury kept and capped.
    /// </summary>
    public void ApplyAuraStats(BalanceData data)
    {
        uint oldHealth = Health;
        uint oldPower = Power ?? 0;
        DerivedCharacterStats next = data.CharacterStats(Class, Level, Worn, AuraTotals());

        Stats = next;
        RegenStat = PowerRegen.StatOf(Class, next);
        Attack = next.AttackerAt(Level);
        Defence = next.Defence;
        HastePct = next.EffectiveHastePct(data.Combat.Formula);
        Health = next.MaxHealth;
        CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, oldHealth, next.MaxHealth);
        Power = next.MaxPower;
        uint current = CurrentPower ?? 0;
        CurrentPower = PowerType == PowerType.Fury
            ? Math.Min(current, next.MaxPower)
            : CharacterStatsCalculator.KeepShare(current, oldPower, next.MaxPower);
    }

    /// <summary>A character entering the world: full health, a full pool except Fury, which enters empty (#526).</summary>
    public static SimPlayer Create(BalanceData data, CharacterClass characterClass, ushort level, IEnumerable<ItemTemplate> worn)
    {
        List<ItemTemplate> wearing = worn.ToList();
        DerivedCharacterStats stats = data.CharacterStats(characterClass, level, wearing);
        PowerType pool = ClassPowerType.Of(characterClass);
        var player = new SimPlayer
        {
            Name = characterClass.ToString(),
            Class = characterClass,
            Stats = stats,
            Worn = wearing,
            RegenStat = PowerRegen.StatOf(characterClass, stats),
            Attack = stats.AttackerAt(level),
            Defence = stats.Defence,
            HastePct = stats.EffectiveHastePct(data.Combat.Formula),
            Level = level,
            Health = stats.MaxHealth,
            CurrentHealth = stats.MaxHealth,
            PowerType = pool,
            Power = stats.MaxPower,
            CurrentPower = PowerPool.EmptiesOnReset(pool) ? 0u : stats.MaxPower,
        };
        player.Abilities.AddRange(data.KitOf(characterClass).Select(t => new SimAbility(t)));
        return player;
    }
}
