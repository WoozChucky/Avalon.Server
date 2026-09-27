using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World;
using Avalon.World.Combat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Creatures;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Combat;

/// <summary>
/// #526: a caster gains its ability's PowerGainPerHit per unit the ability damages, and a Fury
/// character gains a share of the health it loses. Driven through the real MapInstance, the cast
/// handler, the shape scripts and CombatService. Character ids 526_1xx and 526_2xx, creature ids 526_9xx.
/// </summary>
public class FuryGainShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    /// <summary>Cleave as seeded: a cone in front, cost 0, 8 Fury per unit damaged.</summary>
    private static AbilityTemplate Cleave()
    {
        AbilityTemplate cleave = AbilityTestData.Cone(200, reach: 2.5f, arc: 100f);
        cleave.PowerGainPerHit = 8;
        return cleave;
    }

    /// <summary>Ground Slam as seeded: a circle on the caster, costing 20, gaining nothing.</summary>
    private static AbilityTemplate GroundSlam()
    {
        AbilityTemplate slam = AbilityTestData.Circle(201, radius: 3f);
        slam.Cost = 20;
        return slam;
    }

    /// <summary>A warrior with 100 health and a 100 Fury pool holding <paramref name="fury" />, facing +Z.</summary>
    private static MapInstanceClient Warrior(MapInstance instance, uint id, uint fury = 0, params AbilityTemplate[] abilities)
    {
        MapInstanceClient warrior = Join(instance, id);
        warrior.Character.PowerType = PowerType.Fury;
        warrior.Character.Power = 100;
        warrior.Character.CurrentPower = fury;
        warrior.Character.Health = 100;
        warrior.Character.CurrentHealth = 100;
        warrior.Character.Orientation = new Vector3(0f, 0f, 0f);
        warrior.Character.Spells.Load(abilities.Select(AbilityTestData.Game).ToArray());
        return warrior;
    }

    private static Creature AddCreature(MapInstance instance, uint id, Vector3 position, uint health = 50)
    {
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = position,
            Health = health,
            CurrentHealth = health,
        };
        creature.Script = new MapInstanceAbilityCastShould.WoundScript(creature);
        instance.AddCreature(creature);
        return creature;
    }

    /// <summary>Three creatures inside Cleave's cone.</summary>
    private static Creature[] ThreeInFront(MapInstance instance, uint firstId) =>
    [
        AddCreature(instance, firstId, new Vector3(0f, 0f, 2f)),
        AddCreature(instance, firstId + 1, new Vector3(0.6f, 0f, 1.5f)),
        AddCreature(instance, firstId + 2, new Vector3(-0.6f, 0f, 1.5f)),
    ];

    private static ICreature Attacker(uint id)
    {
        var attacker = Substitute.For<ICreature>();
        attacker.Guid.Returns(new ObjectGuid(ObjectType.Creature, id));
        return attacker;
    }

    [Fact]
    public void Gain_8_Fury_Per_Unit_Cleave_Damages()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Warrior(instance, 526_101, fury: 0, Cleave());
        Creature[] targets = ThreeInFront(instance, 526_901);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Empty(warrior.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.All(targets, t => Assert.Equal(40u, t.CurrentHealth));
        Assert.Equal(24u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Gain_Nothing_When_Cleave_Damages_Nobody()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Warrior(instance, 526_111, fury: 0, Cleave());
        Creature behind = AddCreature(instance, 526_911, new Vector3(0f, 0f, -2f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Empty(warrior.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Equal(50u, behind.CurrentHealth);
        Assert.Equal(0u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Cap_The_Gain_At_The_Pool_Maximum()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Warrior(instance, 526_121, fury: 95, Cleave());
        ThreeInFront(instance, 526_921);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });

        Assert.Equal(100u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Gain_Nothing_From_Ground_Slam()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Warrior(instance, 526_131, fury: 50, GroundSlam());
        Creature target = AddCreature(instance, 526_931, new Vector3(1f, 0f, 0f));

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 201 });

        Assert.Equal(40u, target.CurrentHealth);
        Assert.Equal(30u, warrior.Character.CurrentPower);   // paid 20, gained nothing
    }

    /// <summary>
    /// The early returns in CombatService: nothing was damaged, so nothing is gained. A corpse never
    /// reaches the combat service through a shape (the hit query skips the dead), so each target is also
    /// hit directly with Cleave.
    /// </summary>
    [Fact]
    public void Gain_Nothing_From_An_Invulnerable_A_Dead_Or_A_Returning_Target()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Warrior(instance, 526_141, fury: 0, Cleave());
        Creature npc = AddCreature(instance, 526_941, new Vector3(0f, 0f, 2f));
        npc.Invulnerable = true;
        Creature corpse = AddCreature(instance, 526_942, new Vector3(0.6f, 0f, 1.5f));
        corpse.CurrentHealth = 0;
        Creature returning = CombatServiceShould.CreatureReturningHome(
            Substitute.For<ISimulationContext>(), nameof(CreatureCombatScript), health: 30);
        returning.Position = new Vector3(-0.6f, 0f, 1.5f);
        instance.AddCreature(returning);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });
        var cleave = AbilityTestData.Game(Cleave());
        instance.CombatService.ApplyDamage(warrior.Character, npc, 10, cleave);
        instance.CombatService.ApplyDamage(warrior.Character, corpse, 10, cleave);
        instance.CombatService.ApplyDamage(warrior.Character, returning, 10, cleave);

        Assert.Empty(warrior.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Equal(50u, npc.CurrentHealth);
        Assert.Equal(30u, returning.CurrentHealth);
        Assert.Equal(0u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Gain_Nothing_From_A_Heal()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        AbilityTemplate heal = AbilityTestData.HealCircle(232, reach: 15f, radius: 4f);
        heal.PowerGainPerHit = 5;
        MapInstanceClient warrior = Warrior(instance, 526_151, fury: 0, heal);
        MapInstanceClient ally = Join(instance, 526_152);
        ally.Character.Position = new Vector3(0f, 0f, 8f);
        ally.Character.Health = 100;
        ally.Character.CurrentHealth = 40;

        handler.Execute(warrior.Connection,
            new CCastAbilityPacket { AbilityId = 232, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 8f } });

        Assert.Equal(80u, ally.Character.CurrentHealth);
        Assert.Equal(0u, warrior.Character.CurrentPower);
    }

    /// <summary>A piercing projectile gains once per unit it hits, and hits each unit once.</summary>
    [Fact]
    public void Gain_Per_Unit_Pierced()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        AbilityTemplate spear = AbilityTestData.Projectile(202, reach: 20f, speed: 20f, pierce: true);
        spear.PowerGainPerHit = 4;
        MapInstanceClient warrior = Warrior(instance, 526_161, fury: 0, spear);
        Creature near = AddCreature(instance, 526_961, new Vector3(0f, 0f, 4f));
        Creature far = AddCreature(instance, 526_962, new Vector3(0f, 0f, 8f));

        handler.Execute(warrior.Connection,
            new CCastAbilityPacket { AbilityId = 202, GroundPos = new Vector3Dto { X = 0f, Y = 0f, Z = 20f } });
        for (int i = 0; i < 90; i++)
        {
            instance.Update(Tick);
        }

        Assert.Equal(40u, near.CurrentHealth);
        Assert.Equal(40u, far.CurrentHealth);
        Assert.Equal(8u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Gain_Fury_From_Damage_Taken_By_Share_Of_Max_Health()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient warrior = Warrior(instance, 526_171);

        instance.CombatService.ApplyDamage(Attacker(526_971), warrior.Character, 10);

        Assert.Equal(90u, warrior.Character.CurrentHealth);
        Assert.Equal(5u, warrior.Character.CurrentPower);
    }

    /// <summary>Game:FuryFromDamageTaken reaches the instance's combat service: at 20, a tenth of max health gives 2.</summary>
    [Fact]
    public void Use_The_Configured_Share_For_Damage_Taken()
    {
        IWorld world = NewWorld();
        world.Configuration.Returns(new GameConfiguration { FuryFromDamageTaken = 20f });
        using MapInstance instance = TestMapInstances.Build(world);
        MapInstanceClient warrior = Warrior(instance, 526_172);

        instance.CombatService.ApplyDamage(Attacker(526_972), warrior.Character, 10);

        Assert.Equal(2u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Round_The_Damage_Taken_Gain_Down()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient warrior = Warrior(instance, 526_181);

        instance.CombatService.ApplyDamage(Attacker(526_981), warrior.Character, 3);

        Assert.Equal(1u, warrior.Character.CurrentPower);   // 1.5 rounded down
    }

    /// <summary>
    /// Only the health actually lost counts: 4 of 100 at factor 50 is 2, not the 25 a hit of 50 would
    /// give. Overkill always kills, and death empties Fury, so the cap is pinned on the formula.
    /// </summary>
    [Theory]
    [InlineData(4u, 50u, 2u)]
    [InlineData(100u, 10u, 5u)]
    [InlineData(100u, 3u, 1u)]
    [InlineData(100u, 250u, 50u)]
    public void Count_Only_The_Health_Actually_Lost(uint healthBefore, uint damage, uint expected)
    {
        Assert.Equal(expected, Fury.FromDamageTaken(damage, healthBefore, maxHealth: 100, factor: 50f));
    }

    [Fact]
    public void Gain_Nothing_From_Damage_Taken_With_No_Maximum_Health_Or_Factor_And_Never_Wrap()
    {
        Assert.Equal(0u, Fury.FromDamageTaken(10, 10, maxHealth: 0, factor: 50f));
        Assert.Equal(0u, Fury.FromDamageTaken(10, 10, maxHealth: 100, factor: 0f));
        Assert.Equal(uint.MaxValue, Fury.FromDamageTaken(10, 10, maxHealth: 1, factor: float.MaxValue));
    }

    [Fact]
    public void Gain_Nothing_When_Already_Dead()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient warrior = Warrior(instance, 526_191, fury: 0);
        warrior.Character.CurrentHealth = 0;
        warrior.Character.IsDead = true;

        instance.CombatService.ApplyDamage(Attacker(526_991), warrior.Character, 10);

        Assert.Equal(0u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Gain_Nothing_From_Damage_Taken_Without_A_Fury_Pool()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient wizard = Warrior(instance, 526_201, fury: 30);
        wizard.Character.PowerType = PowerType.Mana;

        instance.CombatService.ApplyDamage(Attacker(526_992), wizard.Character, 10);

        Assert.Equal(90u, wizard.Character.CurrentHealth);
        Assert.Equal(30u, wizard.Character.CurrentPower);
    }

    [Fact]
    public void Count_Player_Damage_Too()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient attacker = Join(instance, 526_211);
        MapInstanceClient warrior = Warrior(instance, 526_212);
        attacker.Character.Data!.PvpEnabled = true;
        warrior.Character.Data!.PvpEnabled = true;
        warrior.Character.Position = new Vector3(1f, 0f, 0f);
        attacker.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.Circle(201, radius: 3f))]);

        handler.Execute(attacker.Connection, new CCastAbilityPacket { AbilityId = 201 });

        Assert.Equal(90u, warrior.Character.CurrentHealth);
        Assert.Equal(5u, warrior.Character.CurrentPower);
    }

    [Fact]
    public void Cap_The_Damage_Taken_Gain_At_The_Pool_Maximum()
    {
        using MapInstance instance = TestMapInstances.Build(NewWorld());
        MapInstanceClient warrior = Warrior(instance, 526_221, fury: 98);

        instance.CombatService.ApplyDamage(Attacker(526_993), warrior.Character, 10);

        Assert.Equal(100u, warrior.Character.CurrentPower);
    }

    /// <summary>Review focus: at 0 Fury Cleave still casts and builds; Ground Slam waits for 20.</summary>
    [Fact]
    public void Keep_Casting_Cleave_At_Zero_Fury()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler handler);
        MapInstanceClient warrior = Warrior(instance, 526_231, fury: 0, Cleave(), GroundSlam());
        ThreeInFront(instance, 526_994);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 201 });
        SAbilityNotReadyPacket refused = Assert.Single(warrior.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));
        Assert.Equal(CastRejectReason.NotEnoughPower, refused.Reason);

        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 200 });
        Assert.Equal(24u, warrior.Character.CurrentPower);

        warrior.Character.LastCastStartTime = DateTime.UtcNow.AddSeconds(-1);   // past the global cooldown
        handler.Execute(warrior.Connection, new CCastAbilityPacket { AbilityId = 201 });
        Assert.Single(warrior.Read<SAbilityNotReadyPacket>(NetworkPacketType.SMSG_ABILITY_NOT_READY));   // only the first
        Assert.Equal(4u, warrior.Character.CurrentPower);
    }
}
