using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// #163: the combat script attacks with the creature's abilities. <c>ChooseAbility</c> picks; the base starts
/// what it picks when it is ready and in reach; a wind-up stands still until its cast ends; a creature walking
/// home casts nothing; and the chained scripts still cast. Driven over a substitute context the
/// <see cref="CastRig" /> plays the cast system for, except where a real instance is needed.
/// </summary>
public class CreatureAbilityAiShould
{
    private static readonly AbilityId SpecialId = new(90_164);
    private static readonly AbilityId WindUpId = new(90_165);

    /// <summary>A 2.5 m cone on an 8 s cooldown.</summary>
    private static AbilityTemplate Special()
    {
        AbilityTemplate row = AbilityTestData.Cone(SpecialId.Value, reach: 2.5f, arc: 90f);
        row.Name = "Special";
        row.Cooldown = 8000;
        row.AllowedClasses = [];
        return row;
    }

    /// <summary>A 1 s wind-up, a 5 m circle on the creature, 15 s.</summary>
    private static AbilityTemplate WindUp()
    {
        AbilityTemplate row = AbilityTestData.Circle(WindUpId.Value, radius: 5f);
        row.Name = "Wind-up";
        row.CastTime = 1000;
        row.Cooldown = 15000;
        row.AllowedClasses = [];
        return row;
    }

    private sealed class Fight
    {
        public Fight(Vector3 targetAt, bool withRotation = true, AbilityId? prefer = null,
            Avalon.World.Abilities.AbilityCatalog? catalog = null, CreatureAbilityKit? kit = null)
        {
            Creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 163_1));
            Creature.Position.Returns(Vector3.zero);
            Creature.TauntedBy = null;
            Creature.TauntExpiresAt = DateTime.MinValue;
            Creature.Metadata.Returns(Substitute.For<ICreatureMetadata>());
            Creature.Health.Returns(100u);
            Creature.CurrentHealth.Returns(100u);

            Target.Guid.Returns(new ObjectGuid(ObjectType.Character, 163_2));
            Target.Position.Returns(targetAt);
            Target.IsDead.Returns(false);

            Vector3? requested = null;
            Locomotion.When(l => l.MoveTo(Creature, Arg.Any<Vector3>())).Do(ci => requested = ci.ArgAt<Vector3>(1));
            Locomotion.ResolvedDestination(Creature).Returns(_ => requested);
            Locomotion.HasArrived(Creature).Returns(true);

            Combat.GetEncounterFor(Creature).Returns((IEncounter?)null);
            Context.CombatService.Returns(Combat);
            Context.Locomotion.Returns(Locomotion);
            Context.MeleeSlots.Returns(new MeleeSlots(6, radius: 1.5f));

            Script = new KitCombatScript(Creature, Context, catalog: catalog ?? TestKit.Catalog(Special(), WindUp()),
                kit: kit ?? new CreatureAbilityKit(TestKit.BasicId, SpecialId, WindUpId));
            if (withRotation)
            {
                AbilityId first = prefer ?? SpecialId;
                Script.Rotation = (_, distance) => Script.ReadyFor(first, distance) ?? Script.ReadyFor(TestKit.BasicId, distance);
            }

            Script.OnEnteredRange(Target);
        }

        public ICreature Creature { get; } = Substitute.For<ICreature>();
        public ICharacter Target { get; } = Substitute.For<ICharacter>();
        public ICreatureLocomotion Locomotion { get; } = Substitute.For<ICreatureLocomotion>();
        public ICombatService Combat { get; } = Substitute.For<ICombatService>();
        public ISimulationContext Context { get; } = Substitute.For<ISimulationContext>();
        public KitCombatScript Script { get; }
        public CastRig Rig => CastRig.Of(Context);

        public List<AbilityId> Cast() => Rig.Casts.Select(c => c.Ability.AbilityId).ToList();

        public void Tick(double seconds = 0.1) => Script.Update(TimeSpan.FromSeconds(seconds));
    }

    // ── choosing ──

    [Fact]
    public void Prefer_a_ready_special_in_reach_over_the_basic()
    {
        var fight = new Fight(targetAt: new Vector3(1f, 0f, 0f));

        fight.Tick();   // the special
        fight.Tick(3);  // the basic: the special is on its 8 s cooldown
        fight.Tick(3);  // the basic again

        Assert.Equal([SpecialId, TestKit.BasicId, TestKit.BasicId], fight.Cast());
    }

    [Fact]
    public void Cast_a_special_whose_reach_fits_where_the_basic_does_not()
    {
        var fight = new Fight(targetAt: new Vector3(2.2f, 0f, 0f));   // past the basic's 1.8, inside the special's 2.5
        fight.Locomotion.HasArrived(fight.Creature).Returns(false);

        fight.Tick();

        Assert.Equal([SpecialId], fight.Cast());
    }

    [Fact]
    public void Never_recast_an_ability_on_cooldown()
    {
        var fight = new Fight(targetAt: new Vector3(1f, 0f, 0f), withRotation: false);

        fight.Tick();
        for (int i = 0; i < 20; i++)
            fight.Tick();   // two seconds, inside the 2.25 s cooldown

        Assert.Equal([TestKit.BasicId], fight.Cast());
    }

    /// <summary>What ChooseAbility answers is started only if the creature holds it, it is ready, and it reaches.</summary>
    [Fact]
    public void Start_nothing_that_is_not_its_own_ready_and_in_reach()
    {
        var fight = new Fight(targetAt: new Vector3(1f, 0f, 0f), withRotation: false);
        IAbility foreign = AbilityTestData.Game(Special());
        IAbility special = fight.Script.Abilities[SpecialId]!;
        special.CooldownTimer = 5f;

        fight.Script.Rotation = (_, _) => foreign;
        fight.Tick();
        fight.Script.Rotation = (_, _) => special;
        fight.Tick();
        fight.Target.Position.Returns(new Vector3(9f, 0f, 0f));
        fight.Script.Rotation = (_, _) => fight.Script.Abilities.Basic;
        fight.Tick();

        Assert.Empty(fight.Cast());
    }

    // ── where it stands ──

    /// <summary>
    /// Review finding (#163): a creature stands where its basic reaches the target's body from anywhere its
    /// locomotion counts as arrived. A crowd's 0.6 m arrival tolerance round a 1.5 m slot let a Blightfly settle
    /// 2.1 m from the target, beyond Sting's reach (1.5 m plus a 0.5 m body), where it stung empty air for good.
    /// Its slot is pulled in to 1.5 + 0.5 - 0.6 - 0.1 = 1.3 m; a waypoint's 0.1 m tolerance leaves it at 1.5 m.
    /// </summary>
    [Theory]
    [InlineData(0.6f, 1.3f)]
    [InlineData(0.1f, 1.5f)]
    public void Stand_where_its_basic_reaches_from_anywhere_it_counts_as_arrived(float tolerance, float expected)
    {
        AbilityTemplate sting = AbilityTestData.Cone(90_166, reach: 1.5f, arc: 90f);
        sting.Name = "Sting";
        sting.Cooldown = 2250;
        sting.AllowedClasses = [];
        var fight = new Fight(targetAt: new Vector3(10f, 0f, 0f), withRotation: false,
            catalog: TestKit.Catalog(sting), kit: new CreatureAbilityKit(new AbilityId(90_166)));
        fight.Target.BodyRadius.Returns(0.5f);
        fight.Locomotion.ArrivalTolerance(fight.Creature).Returns(tolerance);
        Vector3? destination = null;
        fight.Locomotion.When(l => l.MoveTo(fight.Creature, Arg.Any<Vector3>()))
            .Do(ci => destination = ci.ArgAt<Vector3>(1));

        fight.Tick();

        Assert.NotNull(destination);
        Assert.Equal(expected, Vector3.Distance(destination!.Value, new Vector3(10f, 0f, 0f)), 0.001f);
    }

    // ── wind-ups ──

    [Fact]
    public void Stop_face_the_target_and_announce_a_wind_up()
    {
        var fight = new Fight(targetAt: new Vector3(0f, 0f, 3f), prefer: WindUpId);
        fight.Locomotion.HasArrived(fight.Creature).Returns(false);

        fight.Tick();

        (IUnit caster, AbilityAim aim, IAbility windUp) = Assert.Single(fight.Rig.Casts);
        Assert.Same(fight.Creature, caster);
        Assert.Equal(WindUpId, windUp.AbilityId);
        Assert.Equal(new Vector3(0f, 0f, 1f), aim.Facing);
        fight.Locomotion.Received(1).Stop(fight.Creature);
        fight.Creature.Received().LookAt(new Vector3(0f, 0f, 3f));
    }

    /// <summary>
    /// #716 leaves creatures as they were: a creature's Movement cast carries no cursor point and aims along its
    /// facing toward the target, captured at cast start, whatever way its body faced before.
    /// </summary>
    [Fact]
    public void Aim_a_movement_ability_toward_its_target_with_no_point()
    {
        var fight = new Fight(targetAt: new Vector3(1f, 0f, 0f), prefer: SpecialId);
        fight.Creature.Orientation.Returns(new Vector3(0f, 180f, 0f));   // its body faces -Z

        fight.Tick();

        (_, AbilityAim aim, IAbility special) = Assert.Single(fight.Rig.Casts);
        Assert.Equal(SpecialId, special.AbilityId);
        Assert.Null(aim.Point);
        Assert.Equal(new Vector3(1f, 0f, 0f), aim.Facing);
    }

    /// <summary>Winding up, the creature asks for no movement however far the target runs, then chases again once the cast ends.</summary>
    [Fact]
    public void Ask_for_no_movement_until_the_wind_up_ends()
    {
        var fight = new Fight(targetAt: new Vector3(0f, 0f, 3f), prefer: WindUpId);
        fight.Tick();
        IAbility windUp = fight.Script.Abilities[WindUpId]!;
        fight.Locomotion.ClearReceivedCalls();

        fight.Target.Position.Returns(new Vector3(0f, 0f, 12f));
        fight.Locomotion.HasArrived(fight.Creature).Returns(true);
        for (int i = 0; i < 8; i++)
            fight.Tick();

        fight.Locomotion.DidNotReceiveWithAnyArgs().MoveTo(default!, default);
        fight.Locomotion.DidNotReceiveWithAnyArgs().Stop(default!);
        Assert.Single(fight.Rig.Casts);

        fight.Rig.Complete(fight.Creature, windUp);
        fight.Tick();

        fight.Locomotion.ReceivedWithAnyArgs().MoveTo(default!, default);
    }

    /// <summary>
    /// Review Focus 2: a creature whose target dies mid wind-up turns for home; the cast system then drops the
    /// cast unfired (CreatureCastShould pins the drop).
    /// </summary>
    [Fact]
    public void Turn_for_home_mid_wind_up_when_its_target_dies()
    {
        var fight = new Fight(targetAt: new Vector3(0f, 0f, 3f), prefer: WindUpId);
        fight.Tick();
        fight.Locomotion.ClearReceivedCalls();

        fight.Target.IsDead.Returns(true);
        fight.Tick();

        Assert.Equal((object)CreatureCombatScript.CombatState.Returning, fight.Script.State);
        fight.Locomotion.Received(1).MoveTo(fight.Creature, Vector3.zero);
    }

    [Fact]
    public void Cast_nothing_while_walking_home()
    {
        var fight = new Fight(targetAt: new Vector3(1f, 0f, 0f));
        fight.Script.State = CreatureCombatScript.CombatState.Returning;
        fight.Locomotion.HasArrived(fight.Creature).Returns(false);
        fight.Creature.Position.Returns(new Vector3(0.5f, 0f, 0f));

        for (int i = 0; i < 10; i++)
            fight.Tick();

        Assert.Empty(fight.Cast());
    }

    /// <summary>The next fight after the walk home starts with every cooldown off.</summary>
    [Fact]
    public void Take_every_cooldown_off_once_home()
    {
        var fight = new Fight(targetAt: new Vector3(1f, 0f, 0f));
        fight.Tick();   // the special, 8 s
        fight.Target.IsDead.Returns(true);
        fight.Tick();   // home at once: the creature never left the origin
        fight.Tick();

        Assert.Equal((object)CreatureCombatScript.CombatState.None, fight.Script.State);
        Assert.All(fight.Script.Abilities.All, a => Assert.True(CreatureAbilities.IsReady(a)));
    }

    // ── the chained scripts ──

    [Fact]
    public void Cast_when_chained_in_aggro_and_in_a_patrol()
    {
        foreach (Func<ICreature, ISimulationContext, AiScript> build in new Func<ICreature, ISimulationContext, AiScript>[]
                 {
                     (c, ctx) => new KitAggroDefendScript(c, ctx),
                     (c, ctx) => new KitPatrolScript(c, ctx),
                 })
        {
            var fight = new Fight(targetAt: new Vector3(1f, 0f, 0f), withRotation: false);
            var context = Substitute.For<ISimulationContext>();
            context.CombatService.Returns(fight.Combat);
            context.Locomotion.Returns(fight.Locomotion);
            context.MeleeSlots.Returns(new MeleeSlots(6, radius: 1.5f));
            context.Characters.Returns(new Dictionary<ObjectGuid, ICharacter>());
            AiScript script = build(fight.Creature, context);

            script.OnHit(fight.Target, 1);
            script.Update(TimeSpan.FromSeconds(0.1));

            Assert.Equal([TestKit.BasicId], CastRig.Of(context).Casts.Select(c => c.Ability.AbilityId));
            fight.Combat.Received(1).ApplyDamage(fight.Creature, fight.Target, Arg.Any<uint>(),
                Arg.Is<IAbility>(a => a.AbilityId == TestKit.BasicId));
        }
    }

    // ── through a real instance ──

    /// <summary>A real instance whose navigator walks straight to any destination and lets every ray through.</summary>
    private static MapInstance Instance()
    {
        var scripts = Substitute.For<Avalon.World.Scripts.IScriptManager>();
        scripts.GetAbilityScript(nameof(Avalon.World.Scripts.Abilities.CircleAbilityScript))
            .Returns(typeof(Avalon.World.Scripts.Abilities.CircleAbilityScript));
        var navigator = Substitute.For<Avalon.World.Public.Maps.IMapNavigator>();
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));
        navigator.FindPath(default, default).ReturnsForAnyArgs(ci => new List<Vector3> { ci.ArgAt<Vector3>(1) });
        return TestMapInstances.Build(NewWorld(), scripts, navigator);
    }

    /// <summary>
    /// Review Focus 1 end to end: a creature killed during its own wind-up never lands it, and the players in
    /// its circle take nothing from it, now or later.
    /// </summary>
    [Fact]
    public void Land_nothing_from_a_wind_up_whose_caster_was_killed_during_it()
    {
        using MapInstance instance = Instance();
        MapInstanceClient player = Join(instance, 163_201);
        player.Character.Health = 100;
        player.Character.CurrentHealth = 100;
        player.Character.Position = new Vector3(0f, 0f, 1.5f);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 163_991),
            TemplateId = new CreatureTemplateId(8),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Name = "Alpha",
            Position = Vector3.zero,
            Health = 100,
            CurrentHealth = 100,
            DamageMin = 5,
            DamageMax = 5,
        };
        var script = new KitCombatScript(creature, instance, catalog: TestKit.Catalog(WindUp()),
            kit: new CreatureAbilityKit(TestKit.BasicId, WindUpId));
        script.Rotation = (_, distance) => script.ReadyFor(WindUpId, distance);
        creature.Script = script;
        instance.AddCreature(creature);
        script.OnEnteredRange(player.Character);

        instance.Update(TimeSpan.FromSeconds(0.1));
        Assert.True(creature.Abilities.IsCasting);
        script.OnHit(player.Character, 1000);   // the killing blow, as the combat service hands it over
        for (int i = 0; i < 20; i++)
            instance.Update(TimeSpan.FromSeconds(0.1));

        Assert.Equal(0u, creature.CurrentHealth);
        Assert.False(creature.Abilities.IsCasting);
        Assert.Equal(100u, player.Character.CurrentHealth);
    }

    /// <summary>
    /// A wind-up aimed where the target stood fires there (#163): the creature stands still, so the cast is
    /// not interrupted by its own movement, and a player who stays in the circle takes it.
    /// </summary>
    [Fact]
    public void Land_a_wind_up_on_a_player_who_stayed_in_it()
    {
        using MapInstance instance = Instance();
        MapInstanceClient player = Join(instance, 163_211);
        player.Character.Health = 100;
        player.Character.CurrentHealth = 100;
        player.Character.Position = new Vector3(0f, 0f, 1.5f);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 163_992),
            TemplateId = new CreatureTemplateId(8),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Name = "Alpha",
            Position = Vector3.zero,
            Health = 100,
            CurrentHealth = 100,
        };
        var script = new KitCombatScript(creature, instance, catalog: TestKit.Catalog(WindUp()),
            kit: new CreatureAbilityKit(TestKit.BasicId, WindUpId));
        script.Rotation = (_, distance) => script.ReadyFor(WindUpId, distance);
        creature.Script = script;
        instance.AddCreature(creature);
        script.OnEnteredRange(player.Character);

        for (int i = 0; i < 12; i++)
            instance.Update(TimeSpan.FromSeconds(0.1));

        Assert.Equal(Vector3.zero, creature.Position);
        Assert.True(player.Character.CurrentHealth < 100u);
        Assert.Equal(15f - 0.1f, creature.Abilities[WindUpId]!.CooldownTimer, 0.2f);
    }
}
