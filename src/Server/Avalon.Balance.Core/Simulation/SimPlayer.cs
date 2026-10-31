using Avalon.Combat;
using Avalon.Common;
using Avalon.Network.Packets.State;

namespace Avalon.Balance.Core;

public sealed class SimPlayer : SimUnit
{
    public required CharacterClass Class { get; init; }

    /// <summary>
    /// The build it holds (B-R10): the stat lines and keystone rules every stats refresh folds, and its abilities. Read
    /// only: its totals are the build's own.
    /// </summary>
    public required HeldTalents Talents { get; init; }

    /// <summary>Unbroken (B-R14): its ward takes every hit and tick, blight and bleeds included, and recharges after twice the delay.</summary>
    public override bool WardTakesAll => Talents.Totals.WardTakesAll;

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

    /// <summary>Whether it holds ability <paramref name="abilityId" />.</summary>
    public bool Holds(uint abilityId) => Abilities.Exists(a => a.Id == abilityId);

    /// <summary>As CharacterEntity.GainPower, through the same rule (PowerPool.Gain).</summary>
    public void GainPower(uint amount)
    {
        uint current = CurrentPower ?? 0;
        uint next = PowerPool.Gain(IsDead, current, Power ?? 0, amount);
        if (next != current) CurrentPower = next;
    }

    /// <summary>
    /// As the server's stats refresh after an aura change (CharacterStatsRefresh with KeepShare): the stats again with
    /// the auras and the build's lines folded in (Steady Hand's crit 0 after the auras), mana, health and the ward keeping
    /// their share.
    /// </summary>
    public void ApplyAuraStats(BalanceData data, DateTime now)
    {
        uint oldHealth = Health;
        uint oldPower = Power ?? 0;
        DerivedCharacterStats next = data.CharacterStats(Class, Level, Worn, AuraTotals(), Talents.Totals);

        Stats = next;
        ManaRegen = ManaRegenRates.Of(s_regen, next.MaxPower, next.IncreasedManaRegenPct);
        Attack = next.AttackerAt(Level);
        Defence = next.Defence;
        Pace = CasterPace.From(next.Offence, next.Weapon.AttackTimeMs / 1000f, data.Combat.Formula);
        Health = next.MaxHealth;
        CurrentHealth = CharacterStatsCalculator.KeepShare(CurrentHealth, oldHealth, next.MaxHealth);
        Power = next.MaxPower;
        CurrentPower = CharacterStatsCalculator.KeepShare(CurrentPower ?? 0, oldPower, next.MaxPower);
        SetWardMax(next.MaxWard, now);
    }

    /// <summary>
    /// A character entering the world on <paramref name="talents" />: its lines folded into its stats, the build's
    /// abilities in its order with their shapes (B-R13), full health, full mana (every class on mana, #1181) and a full ward.
    /// A character without a build holds <see cref="TalentTree.Starter" />.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="talents" /> is another class's build.</exception>
    public static SimPlayer Create(BalanceData data, CharacterClass characterClass, ushort level, IEnumerable<WornItem> worn,
        HeldTalents talents)
    {
        if (talents.Class != characterClass)
            throw new ArgumentException($"A {talents.Class} build cannot be held by a {characterClass}", nameof(talents));

        List<WornItem> wearing = [.. worn];
        DerivedCharacterStats stats = data.CharacterStats(characterClass, level, wearing, talents: talents.Totals);
        var player = new SimPlayer
        {
            Name = characterClass.ToString(),
            Class = characterClass,
            Talents = talents,
            Stats = stats,
            Worn = wearing,
            ManaRegen = ManaRegenRates.Of(s_regen, stats.MaxPower, stats.IncreasedManaRegenPct),
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
        player.Abilities.AddRange(talents.Abilities.Select(a => new SimAbility(a.Template, a.Shaping)));
        return player;
    }
}
