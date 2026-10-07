using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Abilities;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Creatures;

/// <summary>
/// #163: a creature casts through the same cast system a character does. Free, gated by cooldowns, its
/// basic waiting its SwingInterval, its casts dropped out loud when it dies or turns for home.
/// Creature ids 163_9xx, character ids 163_1xx.
/// </summary>
public class CreatureCastShould
{
    private static readonly TimeSpan s_tick = TimeSpan.FromSeconds(1d / 60d);
    private static readonly AbilityId s_biteId = new(302);
    private static readonly AbilityId s_clawId = new(303);
    private static readonly AbilityId s_roarId = new(310);

    private readonly IScriptManager _scripts = Substitute.For<IScriptManager>();
    private readonly TestArena _arena = new();
    private readonly InstanceAbilityCastSystem _sut;

    public CreatureCastShould()
    {
        _scripts.GetAbilityScript(nameof(ConeAbilityScript)).Returns(typeof(ConeAbilityScript));
        _scripts.GetAbilityScript(nameof(CircleAbilityScript)).Returns(typeof(CircleAbilityScript));
        _scripts.GetAbilityScript(nameof(ProjectileAbilityScript)).Returns(typeof(ProjectileAbilityScript));
        _sut = new InstanceAbilityCastSystem(NullLoggerFactory.Instance, Substitute.For<IServiceProvider>(), _scripts, _arena);
    }

    /// <summary>Bite, a 1.8 m cone, the wolf's basic.</summary>
    internal static AbilityTemplate Bite()
    {
        AbilityTemplate row = AbilityTestData.Cone(s_biteId.Value, reach: 1.8f, arc: 90f);
        row.EffectValue = 0;
        row.BaseDamageCoefficient = 1.0f;
        row.Cooldown = 2250;
        row.AllowedClasses = [];
        return row;
    }

    /// <summary>Ravenous Claw, a 2.5 m cone, 8 s.</summary>
    private static AbilityTemplate Claw()
    {
        AbilityTemplate row = AbilityTestData.Cone(s_clawId.Value, reach: 2.5f, arc: 90f);
        row.EffectValue = 0;
        row.BaseDamageCoefficient = 1.8f;
        row.Cooldown = 8000;
        row.AllowedClasses = [];
        return row;
    }

    /// <summary>A 1 s wind-up, a 5 m circle on the creature, with a cost a creature never pays.</summary>
    private static AbilityTemplate Roar()
    {
        AbilityTemplate row = AbilityTestData.Circle(s_roarId.Value, radius: 5f);
        row.CastTime = 1000;
        row.Cooldown = 15000;
        row.Cost = 30;
        row.CostPowerType = PowerType.Mana;   // a creature pays it from no pool at all
        row.AllowedClasses = [];
        return row;
    }

    private static Creature Wolf(uint id = 163_901, float x = 0f, float z = 0f, float haste = 0f)
    {
        var wolf = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            TemplateId = new CreatureTemplateId(5),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Name = "Grey Fen Wolf",
            Position = new Vector3(x, 0f, z),
            Health = 100,
            CurrentHealth = 100,
            BaseAttackTime = 2.25f,
            HastePct = haste,
            DamageMin = 3,
            DamageMax = 7,
        };
        wolf.Abilities.Load(CreatureAbilitiesShould.Catalog(Bite(), Claw(), Roar()),
            new CreatureAbilityKit(s_biteId, s_clawId, s_roarId), NullLogger.Instance, wolf.Name);
        return wolf;
    }

    private static AbilityAim Along(Vector3 facing) => new(facing, null);

    private static readonly AbilityAim s_north = Along(new Vector3(0f, 0f, 1f));

    // ── hostility through the real shape scripts ──

    [Fact]
    public void Hit_a_player_in_its_cone_but_not_a_creature_or_an_invulnerable_npc()
    {
        Creature wolf = Wolf();
        CharacterEntity player = _arena.Player(163_101, 0f, 1.5f);
        _arena.Creature(0.5f, 1.2f);
        _arena.Creature(-0.5f, 1.2f, invulnerable: true);

        Assert.True(_sut.RunInstant(wolf, s_north, wolf.Abilities[s_biteId]!));

        Assert.Equal([player], _arena.Damaged());
    }

    [Fact]
    public void Hit_each_player_in_its_circle_once_and_no_creature()
    {
        Creature wolf = Wolf();
        IAbility roar = wolf.Abilities[s_roarId]!;
        CharacterEntity near = _arena.Player(163_111, 1f, 0f);
        CharacterEntity far = _arena.Player(163_112, 0f, -4f);
        _arena.Creature(2f, 2f);
        _arena.Creature(-2f, 0f, invulnerable: true);

        Assert.True(_sut.QueueAbility(wolf, s_north, roar));
        RunFor(1.05f);

        Assert.Equal(2, _arena.Damaged().Count);
        Assert.Equal([near, far], _arena.Damaged().OrderBy(u => u.Guid.Id));
    }

    // ── cost ──

    [Fact]
    public void Charge_a_creature_nothing_on_either_path()
    {
        Creature wolf = Wolf();
        wolf.PowerType = Avalon.Network.Packets.State.PowerType.None;
        wolf.CurrentPower = 0;

        Assert.True(_sut.QueueAbility(wolf, s_north, wolf.Abilities[s_roarId]!));   // Cost 30
        Assert.Equal(0u, wolf.CurrentPower);
        Assert.True(wolf.Abilities.IsCasting);
    }

    // ── cooldowns and haste ──

    [Fact]
    public void Wait_the_swing_interval_after_the_basic()
    {
        Creature wolf = Wolf();
        IAbility bite = wolf.Abilities[s_biteId]!;

        Assert.True(_sut.RunInstant(wolf, s_north, bite));

        Assert.Equal(wolf.SwingInterval, bite.CooldownTimer);
        Assert.Equal(2.25f, bite.CooldownTimer);
    }

    /// <summary>
    /// With 50 % haste the basic waits BaseAttackTime / 1.5: SwingInterval already carries the haste, so it is
    /// not divided a second time (Review Focus 5). A special's row cooldown is divided once.
    /// </summary>
    [Fact]
    public void Scale_the_basic_and_the_specials_by_haste_exactly_once()
    {
        Creature wolf = Wolf(haste: 50f);
        IAbility bite = wolf.Abilities[s_biteId]!;
        IAbility claw = wolf.Abilities[s_clawId]!;

        Assert.True(_sut.RunInstant(wolf, s_north, bite));
        Assert.True(_sut.RunInstant(wolf, s_north, claw));

        Assert.Equal(1.5f, bite.CooldownTimer, 0.0001f);
        Assert.Equal(8f / 1.5f, claw.CooldownTimer, 0.0001f);
    }

    [Fact]
    public void Scale_a_wind_up_by_haste()
    {
        Creature wolf = Wolf(haste: 50f);
        IAbility roar = wolf.Abilities[s_roarId]!;

        Assert.True(_sut.QueueAbility(wolf, s_north, roar));

        Assert.Equal(1f / 1.5f, roar.CastTimeTimer, 0.0001f);
    }

    // ── wind-ups ──

    [Fact]
    public void Fire_a_wind_up_once_its_cast_time_has_run_out()
    {
        Creature wolf = Wolf();
        IAbility roar = wolf.Abilities[s_roarId]!;
        CharacterEntity player = _arena.Player(163_121, 0f, 2f);

        Assert.True(_sut.QueueAbility(wolf, s_north, roar));
        RunFor(0.9f);
        Assert.Empty(_arena.Damaged());
        Assert.True(wolf.Abilities.IsCasting);

        RunFor(0.15f);
        Assert.Equal([player], _arena.Damaged());
        Assert.False(wolf.Abilities.IsCasting);
        Assert.Equal(15f, roar.CooldownTimer);
    }

    /// <summary>
    /// A wind-up is dodgeable (#163): its shape is resolved where it fires, so a player who stepped out of it
    /// during the cast takes nothing, and one who stayed takes it.
    /// </summary>
    [Fact]
    public void Hit_only_the_player_who_stayed_in_a_wind_up()
    {
        Creature wolf = Wolf();
        CharacterEntity stayed = _arena.Player(163_191, 0f, 2f);
        CharacterEntity stepped = _arena.Player(163_192, 2f, 0f);

        Assert.True(_sut.QueueAbility(wolf, s_north, wolf.Abilities[s_roarId]!));
        RunFor(0.5f);
        stepped.Position = new Vector3(8f, 0f, 0f);
        RunFor(0.6f);

        Assert.Equal([stayed], _arena.Damaged());
    }

    /// <summary>Review Focus 1: a creature killed mid wind-up casts nothing, now or later, and its cast bar ends at once.</summary>
    [Fact]
    public void Drop_the_wind_up_of_a_creature_killed_mid_cast_and_broadcast_the_interrupt()
    {
        Creature wolf = Wolf();
        IAbility roar = wolf.Abilities[s_roarId]!;
        _arena.Player(163_131, 0f, 2f);

        Assert.True(_sut.QueueAbility(wolf, s_north, roar));
        RunFor(0.3f);
        wolf.CurrentHealth = 0;
        _sut.Update(s_tick, []);

        Assert.Equal([roar], _arena.InterruptsOf(wolf));
        Assert.False(wolf.Abilities.IsCasting);

        RunFor(2f);
        Assert.Empty(_arena.Damaged());
        Assert.Empty(_arena.Finished);
    }

    /// <summary>Review Focus 2: a wind-up in progress when the creature turns for home ends without firing.</summary>
    [Fact]
    public void Drop_the_wind_up_of_a_creature_walking_home()
    {
        Creature wolf = Wolf();
        var combat = new CreatureCombatScript(NullLoggerFactory.Instance, wolf, Substitute.For<ISimulationContext>());
        wolf.Script = combat;
        IAbility roar = wolf.Abilities[s_roarId]!;
        _arena.Player(163_141, 0f, 2f);

        Assert.True(_sut.QueueAbility(wolf, s_north, roar));
        combat.State = CreatureCombatScript.CombatState.Returning;
        RunFor(1.5f);

        Assert.Equal([roar], _arena.InterruptsOf(wolf));
        Assert.Empty(_arena.Damaged());
        Assert.False(wolf.Abilities.IsCasting);
    }

    /// <summary>
    /// A creature is never interrupted by being moved (#163): its script asks for no movement while it winds
    /// up, so a push (a crowd's separation) must not cancel the wind-up, and re-queue it, every tick.
    /// </summary>
    [Fact]
    public void Fire_a_wind_up_whose_creature_was_pushed_during_it()
    {
        Creature wolf = Wolf();
        CharacterEntity player = _arena.Player(163_195, 0f, 2f);

        Assert.True(_sut.QueueAbility(wolf, s_north, wolf.Abilities[s_roarId]!));
        RunFor(0.3f);
        wolf.Position = new Vector3(0.3f, 0f, 0f);
        RunFor(0.8f);

        Assert.Empty(_arena.Interrupted);
        Assert.Equal([player], _arena.Damaged());
    }

    /// <summary>
    /// Review finding (#163): a projectile a creature loosed is dropped the moment it dies or turns for home, so
    /// no corpse, and no creature walking home, deals damage or is put back into an encounter by it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Drop_a_projectile_in_flight_when_its_creature_dies_or_turns_for_home(bool dies)
    {
        Creature wolf = Wolf();
        var combat = new CreatureCombatScript(NullLoggerFactory.Instance, wolf, Substitute.For<ISimulationContext>());
        wolf.Script = combat;
        AbilityTemplate row = AbilityTestData.Projectile(163_305, reach: 10f, speed: 5f);
        row.AllowedClasses = [];
        IAbility spit = AbilityTestData.Game(row);
        _arena.Player(163_196, 0f, 6f);

        Assert.True(_sut.RunInstant(wolf, new AbilityAim(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 6f)), spit));
        RunFor(0.2f);
        if (dies)
            wolf.CurrentHealth = 0;
        else
            combat.State = CreatureCombatScript.CombatState.Returning;

        var objects = new List<IWorldObject>();
        for (int i = 0; i < 180; i++)
        {
            objects.Clear();
            _sut.Update(s_tick, objects);
        }

        Assert.Empty(_arena.Damaged());
        Assert.Empty(objects);   // no longer a world object: every client is sent its removal
    }

    // ── damage through the real combat service ──

    /// <summary>The balance pin: Bite (0 + 1.0 x a roll) rolls the creature's natural range, as the old swing did.</summary>
    [Fact]
    public void Roll_bite_over_the_creatures_natural_damage_range()
    {
        ScriptedCombatRandom rng = ScriptedCombatRandom.Plain().Longs(6);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _, random: rng);
        MapInstanceClient player = Join(instance, 163_151);
        player.Character.Health = 100;
        player.Character.CurrentHealth = 100;
        player.Character.Position = new Vector3(0f, 0f, 1.5f);
        Creature wolf = Wolf();
        instance.AddCreature(wolf);
        uint before = player.Character.CurrentHealth;

        Assert.True(instance.RunInstantAbility(wolf, s_north, wolf.Abilities[s_biteId]!));

        Assert.Equal([(3L, 7L)], rng.WeaponRolls);
        Assert.Equal(before - 6u, player.Character.CurrentHealth);
        SCharacterDamagePacket hit = Assert.Single(player.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal(s_biteId.Value, hit.AbilityId);
    }

    [Fact]
    public void Mark_a_creature_abilitys_crit_on_the_hit()
    {
        ScriptedCombatRandom rng = new ScriptedCombatRandom(0.99, 0.0, 0.99).Longs(4);
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _, random: rng);
        MapInstanceClient player = Join(instance, 163_161);
        player.Character.Health = 100;
        player.Character.CurrentHealth = 100;
        player.Character.Position = new Vector3(0f, 0f, 1.5f);
        var wolf = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 163_961),
            TemplateId = new CreatureTemplateId(5),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Name = "Wolf",
            Position = Vector3.zero,
            Health = 100,
            CurrentHealth = 100,
            DamageMin = 3,
            DamageMax = 7,
            CritPct = 100f,
        };
        wolf.Abilities.Load(CreatureAbilitiesShould.Catalog(Bite()), new CreatureAbilityKit(s_biteId), NullLogger.Instance, "Wolf");
        instance.AddCreature(wolf);

        Assert.True(instance.RunInstantAbility(wolf, s_north, wolf.Abilities[s_biteId]!));

        SCharacterDamagePacket hit = Assert.Single(player.Read<SCharacterDamagePacket>(NetworkPacketType.SMSG_CHARACTER_DAMAGED));
        Assert.Equal(HitResult.Crit, hit.Result);
        Assert.Equal(s_biteId.Value, hit.AbilityId);
    }

    /// <summary>A creature removed mid wind-up (its corpse removed, a script hot reload) never fires it later.</summary>
    [Fact]
    public void Cancel_the_wind_up_of_a_creature_removed_from_the_instance()
    {
        using MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _);
        MapInstanceClient player = Join(instance, 163_181);
        player.Character.Health = 100;
        player.Character.CurrentHealth = 100;
        player.Character.Position = new Vector3(0f, 0f, 2f);
        Creature wolf = Wolf();
        instance.AddCreature(wolf);
        uint before = player.Character.CurrentHealth;
        Assert.True(instance.QueueAbility(wolf, s_north, wolf.Abilities[s_roarId]!));

        instance.RemoveCreature(wolf);
        instance.Update(TimeSpan.FromSeconds(1.5));

        Assert.False(wolf.Abilities.IsCasting);
        Assert.Equal(before, player.Character.CurrentHealth);
    }

    private void RunFor(float seconds)
    {
        for (float t = 0f; t < seconds; t += (float)s_tick.TotalSeconds)
            _sut.Update(s_tick, []);
    }
}
