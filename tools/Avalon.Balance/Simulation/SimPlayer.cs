using Avalon.Balance.Data;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Characters;
using Avalon.World.Combat;
using Avalon.World.Public.Enums;

namespace Avalon.Balance.Simulation;

public sealed class SimPlayer : SimUnit
{
    public required CharacterClass Class { get; init; }

    public required DerivedCharacterStats Stats { get; init; }

    public required uint RegenStat { get; init; }

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

    /// <summary>A character entering the world: full health, a full pool except Fury, which enters empty (#526).</summary>
    public static SimPlayer Create(BalanceData data, CharacterClass characterClass, ushort level, IEnumerable<ItemTemplate> worn)
    {
        DerivedCharacterStats stats = data.CharacterStats(characterClass, level, worn);
        PowerType pool = ClassPowerType.Of(characterClass);
        var player = new SimPlayer
        {
            Name = characterClass.ToString(),
            Class = characterClass,
            Stats = stats,
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
