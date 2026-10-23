using System.Reflection;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Combat;
using Avalon.World.Abilities;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Enums;
using Avalon.World.Testing.Fakes;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The offence reads that run every tick or every hit (#1178). <see cref="AbilityAmountsFlushUnchanged" />:
/// <c>AbilityAmountsFlusher.Flush</c> for a level-1 Warrior with three abilities on a tick where nothing moved, which
/// WorldServer runs once per tick per connection: it compares the stats refresh and the level the amounts were last
/// checked at and returns. <see cref="CreatureCombatWithAura" />: a creature's <c>Combat</c>, which every hit it lands and
/// every cast it makes reads, while it holds an aura giving 20 % more damage: the crit and the offence block its auras
/// give were folded when the aura came, so the read builds the attacker from them and its natural range.
/// <see cref="CreatureCombatNoAura" />: the same read with no aura. 0 B expected on every row.
/// </summary>
/// <remarks>
/// The flush's character has its stats applied and its amounts sent once in setup, so every iteration is the unchanged
/// tick; setup fails the run if a second flush sends anything. A creature's <c>Combat</c> is World-side and internal, so
/// it is read through a delegate bound to its getter once in setup; the delegate's call is the same on both creature rows.
/// </remarks>
[MemoryDiagnoser]
public class OffenceReadBenchmarks
{
    private static readonly Func<Creature, AttackerCombat> s_combat = typeof(Creature)
        .GetProperty("Combat", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!
        .CreateDelegate<Func<Creature, AttackerCombat>>();

    private QuietConnection _connection = null!;
    private Creature _enraged = null!;
    private Creature _calm = null!;

    [GlobalSetup]
    public void Setup()
    {
        var row = new Character
        {
            Id = new CharacterId(1),
            AccountId = new AccountId(1),
            Name = "Bench",
            Class = CharacterClass.Warrior,
            Level = 1,
            CreationDate = DateTime.UtcNow,
        };
        var character = new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };
        character.Spells.Load([Ability(200, "Cleave", 315f), Ability(201, "Ground Slam", 614f), Ability(203, "Rend", 202f)]);

        OffenceStats offence = default;
        offence.AddIncreased(OffenceStat.PhysicalAttack, 101.5f);
        offence.AttackSpeedPct = 2f;
        var formula = new CombatFormula { AttackSpeedCap = 50f, CastSpeedCap = 50f, CooldownRecoveryCap = 50f };
        character.ApplyStats(new DerivedCharacterStats(MaxHealth: 265, MaxPower: 100, Stamina: 27, Strength: 29,
            Agility: 20, Intellect: 20, Armor: 2, BlockPct: 0f, Evasion: 22, CritPct: 5f,
            Weapon: new WeaponDamage(4, 7, DamageType.Physical, 0, 0, null, 792), Offence: offence),
            CurrentValues.EnterWorld, formula);

        _connection = new QuietConnection(character);
        AbilityAmountsFlusher.Flush(_connection);
        if (_connection.Sent != 1 || AbilityAmountsFlushUnchanged() != 1)
            throw new InvalidOperationException($"The flushes sent {_connection.Sent}, not the one at setup.");

        var more = new AuraStatModifier { Stat = AuraStat.IncreasedDamage, Kind = AuraModifierKind.More, Value = 20f };
        var enraged = AuraStatTotals.Of([0], _ => ((IReadOnlyList<AuraStatModifier>)[more], 1u));
        _enraged = Wolf(1);
        _enraged.ApplyAuraStats(enraged, DateTime.UtcNow);
        _calm = Wolf(2);

        if (Math.Abs(s_combat(_enraged).Offence.More(OffenceStat.Damage) - 1.2) > 1e-6 || CreatureCombatNoAura() != 5f)
            throw new InvalidOperationException("The creatures' attack is not the one expected.");
    }

    [Benchmark]
    public int AbilityAmountsFlushUnchanged()
    {
        AbilityAmountsFlusher.Flush(_connection);
        return _connection.Sent;
    }

    [Benchmark]
    public double CreatureCombatWithAura() => s_combat(_enraged).Offence.More(OffenceStat.Damage);

    [Benchmark]
    public float CreatureCombatNoAura() => s_combat(_calm).CritPct;

    private static GameAbility Ability(uint id, string name, float effectiveness) => new()
    {
        AbilityId = new AbilityId(id),
        Metadata = new AbilityMetadata
        {
            Name = name,
            ScriptName = AbilityRules.ConeScript,
            Shape = AbilityShape.Cone,
            Affects = AbilityAffects.Hostile,
            Effects = SpellEffect.Damage,
            Tags = AbilityTags.Attack | AbilityTags.Melee | AbilityTags.Area,
            DamageType = DamageType.Physical,
            DamageEffectiveness = effectiveness,
            Cooldown = 6f,
        },
        CastTimeTimer = 0f,
        CooldownTimer = 0f,
    };

    private static Creature Wolf(uint id) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, id),
        Level = 3,
        Health = 100,
        CurrentHealth = 100,
        BaseMaxHealth = 100,
        CritPct = 5f,
        DamageMin = 5,
        DamageMax = 9,
        BaseAttackTime = 2.25f,
    };
}
