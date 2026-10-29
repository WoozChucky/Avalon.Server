using Avalon.Combat;
using Avalon.Common;
using Avalon.Network.Packets.State;

namespace Avalon.Balance.Core;

public sealed class SimPlayer : SimUnit
{
    public required CharacterClass Class { get; init; }

    /// <summary>Its derived stats, its auras folded in (set again whenever an aura with modifiers changes).</summary>
    public required DerivedCharacterStats Stats { get; set; }

    /// <summary>As CharacterEntity.ManaRegen: its mana rates from the stored maximum, fixed at each stats refresh (#1181).</summary>
    public required ManaRegenRates ManaRegen { get; set; }

    /// <summary>As CharacterEntity.Pace: its speeds, each capped, and its main hand's attack time, from its derived stats.</summary>
    public CasterPace Pace { get; private set; }

    /// <summary>The fixed copies it wears (B-R4), which every stats refresh reads again.</summary>
    public IReadOnlyList<WornItem> Worn { get; private init; } = [];

    public SimAbility Ability(uint id) =>
        Abilities.FirstOrDefault(a => a.Id == id)
        ?? throw new InvalidOperationException($"{Class} does not hold ability {id}");

    /// <summary>As CharacterEntity.GainPower, through the same rule (PowerPool.Gain).</summary>
    public void GainPower(uint amount)
    {
        uint current = CurrentPower ?? 0;
        uint next = PowerPool.Gain(IsDead, current, Power ?? 0, amount);
        if (next != current) CurrentPower = next;
    }

    /// <summary>
    /// As the server's stats refresh after an aura change (CharacterStatsRefresh with KeepShare): the stats again with
    /// the auras folded in, mana, health and the ward keeping their share.
    /// </summary>
    public void ApplyAuraStats(BalanceData data, DateTime now)
    {
        uint oldHealth = Health;
        uint oldPower = Power ?? 0;
        DerivedCharacterStats next = data.CharacterStats(Class, Level, Worn, AuraTotals());

        Stats = next;
        ManaRegen = ManaRegenRates.Of(Regen, next.MaxPower, next.IncreasedManaRegenPct);
        Attack = next.AttackerAt(Level);
        Defence = next.Defence;
        Pace = CasterPace.From(next.Offence, next.Weapon.AttackTimeMs / 1000f, data.Combat.Formula);
        Health = next.MaxHealth;
        CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, oldHealth, next.MaxHealth);
        Power = next.MaxPower;
        CurrentPower = CharacterStatsCalculator.KeepShare(CurrentPower ?? 0, oldPower, next.MaxPower);
        SetWardMax(next.MaxWard, now);
    }

    /// <summary>A character entering the world: full health, full mana (every class on mana, #1181) and a full ward.</summary>
    public static SimPlayer Create(BalanceData data, CharacterClass characterClass, ushort level, IEnumerable<WornItem> worn)
    {
        List<WornItem> wearing = [.. worn];
        DerivedCharacterStats stats = data.CharacterStats(characterClass, level, wearing);
        var player = new SimPlayer
        {
            Name = characterClass.ToString(),
            Class = characterClass,
            Stats = stats,
            Worn = wearing,
            ManaRegen = ManaRegenRates.Of(Regen, stats.MaxPower, stats.IncreasedManaRegenPct),
            Attack = stats.AttackerAt(level),
            Defence = stats.Defence,
            Pace = CasterPace.From(stats.Offence, stats.Weapon.AttackTimeMs / 1000f, data.Combat.Formula),
            Level = level,
            Health = stats.MaxHealth,
            CurrentHealth = stats.MaxHealth,
            PowerType = PowerType.Mana,
            Power = stats.MaxPower,
            CurrentPower = stats.MaxPower,
        };
        player.ResetWard(stats.MaxWard);
        player.Abilities.AddRange(data.KitOf(characterClass).Select(t => new SimAbility(t)));
        return player;
    }
}
