using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.State;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Scripts.Creatures.Forest;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Scripts;

public class AggroDefendScriptShould
{
    [Theory]
    [InlineData(typeof(AggroDefendScript))]
    [InlineData(typeof(ThornbackBoarScript))]
    [InlineData(typeof(GreyFenWolfScript))]
    [InlineData(typeof(BlightflySwarmlingScript))]
    [InlineData(typeof(HuskOfTheWoldScript))]
    [InlineData(typeof(BramblemawAlphaScript))]
    [InlineData(typeof(OldTuskrootScript))]
    [InlineData(typeof(MotherBrambleScript))]
    public void Preserve_search_rearm_and_attack_wakeup_for_built_in_scripts(Type type)
    {
        var rig = new Rig();
        AiScript script = type == typeof(AggroDefendScript)
            ? new AggroDefendScript(NullLoggerFactory.Instance, rig.Creature, rig.Context)
            : (AiScript)Activator.CreateInstance(type, NullLoggerFactory.Instance, rig.Creature, rig.Context, null, null)!;
        rig.Creature.Script = script;
        for (int i = 0; i < 3; i++)
            script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Equal(MoveState.Idle, rig.Creature.MoveState);
        script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Equal(MoveState.Running, rig.Creature.MoveState);

        script.OnCharacterLeft(rig.Target);
        script.Update(TimeSpan.FromSeconds(0.01)); // Return completes at the unchanged spawn position.
        Assert.Equal(MoveState.Idle, rig.Creature.MoveState);
        for (int i = 0; i < 3; i++)
            script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Equal(MoveState.Idle, rig.Creature.MoveState);
        script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Equal(MoveState.Running, rig.Creature.MoveState);

        foreach (bool dodged in new[] { false, true })
        {
            script.OnCharacterLeft(rig.Target);
            script.Update(TimeSpan.FromSeconds(0.01));
            script.Update(TimeSpan.FromSeconds(0.01)); // Inside the next search's waiting interval.
            Assert.Equal(MoveState.Idle, rig.Creature.MoveState);
            if (dodged)
                script.OnAttacked(rig.Target);
            else
                script.OnHit(rig.Target, 1);
            script.Update(TimeSpan.FromSeconds(0.01));
            Assert.Equal(MoveState.Running, rig.Creature.MoveState);
        }
    }

    [Fact]
    public void Search_on_the_existing_interval_and_run_combat_on_the_detection_tick()
    {
        var rig = new Rig();
        for (int i = 0; i < 3; i++)
            rig.Script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Null(rig.Combat.CurrentTarget);
        Assert.Equal(0, rig.Combat.Updates);

        rig.Script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Same(rig.Target, rig.Combat.CurrentTarget);
        Assert.Equal(1, rig.Combat.Updates);

        rig.Script.Update(TimeSpan.FromSeconds(0.01));
        Assert.Equal(2, rig.Combat.Updates);

        rig.Script.OnCharacterLeft(rig.Target);
        Assert.Equal(CreatureCombatScript.CombatState.Returning, rig.Combat.State);
        rig.Script.Update(TimeSpan.FromSeconds(0.25)); // Already at home: return completes.
        Assert.Equal(CreatureCombatScript.CombatState.None, rig.Combat.State);
        for (int i = 0; i < 3; i++)
            rig.Script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Null(rig.Combat.CurrentTarget);
        rig.Script.Update(TimeSpan.FromSeconds(0.25));
        Assert.Same(rig.Target, rig.Combat.CurrentTarget);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Respond_to_a_hit_or_dodged_attack_before_the_next_search(bool dodged)
    {
        var rig = new Rig();
        rig.Script.Update(TimeSpan.FromSeconds(0.01));
        if (dodged)
            rig.Script.OnAttacked(rig.Target);
        else
            rig.Script.OnHit(rig.Target, 1);
        rig.Script.Update(TimeSpan.FromSeconds(0.01));

        Assert.Same(rig.Target, rig.Combat.CurrentTarget);
        Assert.Equal(1, rig.Combat.Updates);
        Assert.Equal(dodged ? 100u : 99u, rig.Creature.CurrentHealth);
    }

    [Fact]
    public void Honor_a_custom_combat_run_predicate_even_in_the_idle_state()
    {
        var rig = new Rig();
        rig.Combat.RunWhileIdle = true;
        rig.Script.Update(TimeSpan.FromSeconds(0.01));

        Assert.Equal(CreatureCombatScript.CombatState.None, rig.Combat.State);
        Assert.Equal(1, rig.Combat.Updates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keep_custom_chain_members_running_between_searches(bool replaceDetector)
    {
        var rig = new Rig();
        var extra = new ExtraScript(rig.Creature, rig.Context);
        rig.Script.Add(extra, replaceDetector);
        for (int i = 0; i < 3; i++)
            rig.Script.Update(TimeSpan.FromSeconds(0.01));
        Assert.Equal(3, extra.Updates);
    }

    private sealed class Rig
    {
        public Rig()
        {
            Creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, 1),
                TemplateId = new CreatureTemplateId(1),
                Metadata = new CreatureTemplate { DetectionRange = 10f },
                Health = 100,
                CurrentHealth = 100,
            };
            Target.Guid.Returns(new ObjectGuid(ObjectType.Character, 2));
            Target.Position.Returns(new Vector3(4f, 0f, 0f));
            var characters = new Dictionary<ObjectGuid, ICharacter> { [Target.Guid] = Target };
            Context.Characters.Returns(characters);
            ICombatService combatService = Substitute.For<ICombatService>();
            combatService.GetEncounterFor(Creature).Returns((IEncounter?)null);
            Context.CombatService.Returns(combatService);
            Context.MeleeSlots.Returns(new MeleeSlots(6, 1.5f));
            ICreatureLocomotion locomotion = Substitute.For<ICreatureLocomotion>();
            locomotion.When(l => l.Stop(Creature)).Do(_ => Creature.MoveState = MoveState.Idle);
            Context.Locomotion.Returns(locomotion);
            IMapNavigator navigator = Substitute.For<IMapNavigator>();
            navigator.HasVisibility(default, default).ReturnsForAnyArgs(true);
            Context.GetNavigatorForPosition(default).ReturnsForAnyArgs(navigator);
            Combat = new ObservedCombat(Creature, Context);
            Script = new ExtendedAggro(Creature, Context, Combat);
        }

        public Creature Creature { get; }
        public ICharacter Target { get; } = Substitute.For<ICharacter>();
        public ISimulationContext Context { get; } = Substitute.For<ISimulationContext>();
        public ObservedCombat Combat { get; }
        public ExtendedAggro Script { get; }
    }

    [ChainedScript]
    private sealed class ObservedCombat(ICreature creature, ISimulationContext context)
        : CreatureCombatScript(NullLoggerFactory.Instance, creature, context)
    {
        public bool RunWhileIdle { get; set; }
        public int Updates { get; private set; }
        public IUnit? CurrentTarget => Target;
        protected override bool ShouldRun() => RunWhileIdle || base.ShouldRun();
        public override void Update(TimeSpan deltaTime)
        {
            Updates++;
            base.Update(deltaTime);
        }
    }

    [ChainedScript]
    private sealed class ExtendedAggro(ICreature creature, ISimulationContext context, CreatureCombatScript combat)
        : AggroDefendScript(creature, context, combat)
    {
        public void Add(AiScript extra, bool replaceDetector)
        {
            if (replaceDetector)
                ChainedScripts[0] = extra;
            else
                Chain(extra);
        }
    }

    [ChainedScript]
    private sealed class ExtraScript(ICreature creature, ISimulationContext context) : AiScript(creature, context)
    {
        public override object State { get; set; } = string.Empty;
        public int Updates { get; private set; }
        protected override bool ShouldRun() => true;
        public override void Update(TimeSpan deltaTime) => Updates++;
    }
}
