using Avalon.Common;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #526: Fury drains out of combat, never in it, and is emptied on death and on every instance
/// transfer. Mana and Energy are untouched. Character ids 526_3xx.
/// </summary>
public class FuryLifecycleShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    private static readonly DateTimeOffset Start = new(2001, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A character with a 100-point <paramref name="pool" /> holding <paramref name="current" />, on <paramref name="clock" />.</summary>
    private static CharacterEntity Character(FixedTimeProvider clock, uint current, PowerType pool = PowerType.Fury,
        float furyDecayPerSecond = 5f, uint regenStat = 0, RegenConfiguration? regen = null)
    {
        var row = new Character { Id = 526_301u, Health = 100, Power1 = 100 };
        var entity = new CharacterEntity(NullLoggerFactory.Instance, row, regen ?? new RegenConfiguration(), clock,
            furyDecayPerSecond)
        {
            Data = row,
            PowerType = pool,
            CurrentHealth = 100,
            CurrentPower = current,
            RegenStat = regenStat,
        };
        entity.Spells.Load(Array.Empty<IAbility>());
        return entity;
    }

    private static void Run(CharacterEntity character, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            character.Update(Tick);
        }
    }

    [Fact]
    public void Not_Decay_In_Combat()
    {
        var clock = new FixedTimeProvider(Start);
        CharacterEntity warrior = Character(clock, current: 50);
        warrior.MarkCombat();

        Run(warrior, 120);

        Assert.Equal(50u, warrior.CurrentPower);
    }

    /// <summary>Review focus: 60 ticks of 1/60 s at 5 per second lose exactly 5, the fraction carried over.</summary>
    [Fact]
    public void Decay_Exactly_The_Rate_Out_Of_Combat()
    {
        var clock = new FixedTimeProvider(Start);
        CharacterEntity warrior = Character(clock, current: 50);
        warrior.MarkCombat();
        clock.Now = Start.AddSeconds(new RegenConfiguration().CombatLeaveDelaySeconds);   // the tag has just ended
        Assert.False(warrior.IsInCombat);

        Run(warrior, 60);

        Assert.Equal(45u, warrior.CurrentPower);
    }

    /// <summary>A fraction left over from a fight is not carried into the next decay.</summary>
    [Fact]
    public void Start_The_Fraction_Over_After_A_Fight()
    {
        var clock = new FixedTimeProvider(Start);
        CharacterEntity warrior = Character(clock, current: 50);
        Run(warrior, 11);   // 55/60 of a point owed, none lost yet
        Assert.Equal(50u, warrior.CurrentPower);

        warrior.MarkCombat();
        Run(warrior, 1);
        clock.Now = Start.AddSeconds(new RegenConfiguration().CombatLeaveDelaySeconds);
        Run(warrior, 11);

        Assert.Equal(50u, warrior.CurrentPower);
    }

    [Fact]
    public void Stop_Decaying_At_Zero()
    {
        var clock = new FixedTimeProvider(Start);
        CharacterEntity warrior = Character(clock, current: 3);

        Run(warrior, 600);

        Assert.Equal(0u, warrior.CurrentPower);
    }

    [Fact]
    public void Not_Decay_When_The_Rate_Is_Zero()
    {
        var clock = new FixedTimeProvider(Start);
        CharacterEntity warrior = Character(clock, current: 50, furyDecayPerSecond: 0f);

        Run(warrior, 600);

        Assert.Equal(50u, warrior.CurrentPower);
    }

    /// <summary>The same expectation as CharacterEntityRegenShould's out-of-combat Mana regeneration.</summary>
    [Theory]
    [InlineData(PowerType.Mana)]
    [InlineData(PowerType.Energy)]
    public void Leave_Mana_And_Energy_Regeneration_Unchanged(PowerType pool)
    {
        var clock = new FixedTimeProvider(Start);
        CharacterEntity caster = Character(clock, current: 50, pool, regenStat: 10,
            regen: new RegenConfiguration { PowerRegenOutOfCombatPerStat = 1.0f });

        caster.Update(TimeSpan.FromSeconds(1));

        Assert.Equal(60u, caster.CurrentPower);
    }

    [Fact]
    public void Zero_Fury_On_Death()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient warrior = Join(instance, 526_311);
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Power = 100;
        warrior.Character.CurrentPower = 60;
        warrior.Character.Health = 100;
        warrior.Character.CurrentHealth = 10;
        var attacker = Substitute.For<ICreature>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Creature, 526_391u));

        instance.CombatService.ApplyDamage(attacker, warrior.Character, 50);

        Assert.True(warrior.Character.IsDead);
        Assert.Equal(0u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Zero_Fury_On_A_Transfer_And_Leave_Mana_And_Energy()
    {
        using MapInstance first = TestMapInstances.Build(NewWorld());
        using MapInstance second = TestMapInstances.Build(NewWorld());
        MapInstanceClient warrior = Join(first, 526_321);
        MapInstanceClient wizard = Join(first, 526_322);
        MapInstanceClient hunter = Join(first, 526_323);
        foreach ((MapInstanceClient client, PowerType pool) in new[]
                 { (warrior, PowerType.Fury), (wizard, PowerType.Mana), (hunter, PowerType.Energy) })
        {
            client.Character.PowerType = pool;
            client.Character.Power = 100;
            client.Character.CurrentPower = 60;
        }

        foreach (MapInstanceClient client in new[] { warrior, wizard, hunter })
        {
            first.RemoveCharacter(client.Connection);
            client.Character.InstanceId = second.InstanceId;
            second.AddCharacter(client.Connection);
        }

        Assert.Equal(0u, warrior.Character.CurrentPower);
        Assert.Equal(60u, wizard.Character.CurrentPower);
        Assert.Equal(60u, hunter.Character.CurrentPower);
    }
}
