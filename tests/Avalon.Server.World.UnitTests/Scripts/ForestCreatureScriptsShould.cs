using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World;
using Avalon.World.Creatures;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Scripts.Creatures.Forest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// #163: each forest creature's own script, over the seeded ability rows. It loads its kit onto the creature,
/// and its rotation prefers a ready special whose reach fits, then its basic. Driven over a substitute
/// context: which ability each tick starts is read from the context's RunInstantAbility and QueueAbility.
/// </summary>
public class ForestCreatureScriptsShould
{
    private static readonly Lazy<Task<StaticData>> Seeded = new(LoadSeededAsync);

    private static async Task<StaticData> LoadSeededAsync()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<AbilityTemplate> abilities = context.AbilityTemplates.AsNoTracking().ToList();

        TestStaticDataRepositories repositories = TestStaticData.Repositories();
        repositories.Abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(abilities));
        return await TestStaticData.LoadAsync(repositories);
    }

    private sealed class Fight
    {
        public Fight(Type scriptType, IWorld world, float targetDistance)
        {
            Creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, 163_500), TemplateId = new CreatureTemplateId(4),
                Metadata = Loot.LootTestData.BoarTemplate(null), Name = scriptType.Name, Position = Vector3.zero,
                Health = 100, CurrentHealth = 100,
            };

            Target.Guid.Returns(new ObjectGuid(ObjectType.Character, 163_501));
            Target.Position.Returns(new Vector3(targetDistance, 0f, 0f));
            Target.BodyRadius.Returns(0.5f);
            Target.IsDead.Returns(false);

            var locomotion = Substitute.For<ICreatureLocomotion>();
            Vector3? requested = null;
            locomotion.When(l => l.MoveTo(Creature, Arg.Any<Vector3>())).Do(ci => requested = ci.ArgAt<Vector3>(1));
            locomotion.ResolvedDestination(Creature).Returns(_ => requested);
            locomotion.HasArrived(Creature).Returns(true);

            var combat = Substitute.For<ICombatService>();
            combat.GetEncounterFor(Creature).Returns((IEncounter?)null);
            Context.CombatService.Returns(combat);
            Context.Locomotion.Returns(locomotion);
            Context.MeleeSlots.Returns(new MeleeSlots(6, radius: 1.5f));
            Context.Characters.Returns(new Dictionary<ObjectGuid, ICharacter>());

            Script = (AiScript)Activator.CreateInstance(scriptType, NullLoggerFactory.Instance, Creature, Context,
                null, world)!;
            Creature.Script = Script;
            Script.OnHit(Target, 1);   // engages it
        }

        public Creature Creature { get; }
        public ICharacter Target { get; } = Substitute.For<ICharacter>();
        public ISimulationContext Context { get; } = Substitute.For<ISimulationContext>();
        public AiScript Script { get; }

        /// <summary>The ability one tick starts, instant or wind-up, or null.</summary>
        public IAbility? Tick()
        {
            Context.ClearReceivedCalls();
            Script.Update(TimeSpan.FromSeconds(0.1));

            return Context.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name is nameof(ISimulationContext.RunInstantAbility)
                    or nameof(ISimulationContext.QueueAbility))
                .Select(c => (IAbility)c.GetArguments()[2]!)
                .SingleOrDefault();
        }

        /// <summary>
        /// Each tick's choice, the choice then put on a long cooldown, until a tick starts nothing: the rotation
        /// in order of preference.
        /// </summary>
        public List<uint> Rotation()
        {
            List<uint> chosen = [];
            while (Tick() is { } ability && chosen.Count < 5)
            {
                chosen.Add(ability.AbilityId.Value);
                ability.CooldownTimer = 100f;
            }

            return chosen;
        }
    }

    private static async Task<IWorld> World()
    {
        StaticData data = await Seeded.Value;
        IWorld world = Substitute.For<IWorld>();
        world.Data.Returns(data);
        return world;
    }

    [Theory]
    [InlineData(typeof(ThornbackBoarScript), new uint[] { 301, 300 })]
    [InlineData(typeof(GreyFenWolfScript), new uint[] { 303, 302 })]
    [InlineData(typeof(HuskOfTheWoldScript), new uint[] { 307, 306 })]
    [InlineData(typeof(BramblemawAlphaScript), new uint[] { 310, 309, 308 })]
    [InlineData(typeof(OldTuskrootScript), new uint[] { 312, 313, 311 })]
    [InlineData(typeof(MotherBrambleScript), new uint[] { 315, 316, 314 })]
    public async Task Prefer_each_ready_special_in_reach_then_the_basic(Type scriptType, uint[] rotation)
    {
        var fight = new Fight(scriptType, await World(), targetDistance: 1.5f);

        Assert.Equal(rotation, fight.Rotation());
        Assert.Equal(rotation.Order(), fight.Creature.Abilities.All.Select(a => a.AbilityId.Value).Order());
    }

    /// <summary>Wind-ups go through the queue: the Alpha's Howling Roar, Tuskroot's Earthsplitter, Mother Bramble's Nova.</summary>
    [Theory]
    [InlineData(typeof(BramblemawAlphaScript), 310u)]
    [InlineData(typeof(OldTuskrootScript), 312u)]
    [InlineData(typeof(MotherBrambleScript), 315u)]
    public async Task Wind_up_its_cast_time_special(Type scriptType, uint windUp)
    {
        var fight = new Fight(scriptType, await World(), targetDistance: 1.5f);

        fight.Script.Update(TimeSpan.FromSeconds(0.1));

        fight.Context.Received(1).QueueAbility(fight.Creature, Arg.Any<AbilityAim>(),
            Arg.Is<IAbility>(a => a.AbilityId.Value == windUp));
    }

    [Fact]
    public async Task Sting_in_melee_and_never_spit_there()
    {
        var fight = new Fight(typeof(BlightflySwarmlingScript), await World(), targetDistance: 1.5f);

        Assert.Equal([304u], fight.Rotation());
    }

    [Fact]
    public async Task Spit_blight_at_a_player_beyond_its_sting_and_within_ten_metres()
    {
        var fight = new Fight(typeof(BlightflySwarmlingScript), await World(), targetDistance: 6f);

        IAbility? spit = fight.Tick();

        Assert.Equal(305u, spit?.AbilityId.Value);
        fight.Context.Received(1).RunInstantAbility(fight.Creature,
            Arg.Is<AbilityAim>(a => a.Point == new Vector3(6f, 0f, 0f)), spit!);
    }

    [Fact]
    public async Task Spit_nothing_at_a_player_past_ten_metres()
    {
        var fight = new Fight(typeof(BlightflySwarmlingScript), await World(), targetDistance: 11f);

        Assert.Null(fight.Tick());
    }

    /// <summary>A kit id the catalog refused is left out, and the creature still fights with the rest.</summary>
    [Fact]
    public async Task Fight_with_the_rest_when_the_catalog_lacks_one_of_its_abilities()
    {
        StaticData seeded = await Seeded.Value;
        List<AbilityTemplate> withoutClaw = seeded.Abilities.Templates.Where(t => t.Id.Value != 303).ToList();
        TestStaticDataRepositories repositories = TestStaticData.Repositories();
        repositories.Abilities.FindAllAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(withoutClaw));
        StaticData data = await TestStaticData.LoadAsync(repositories);
        IWorld world = Substitute.For<IWorld>();
        world.Data.Returns(data);

        var fight = new Fight(typeof(GreyFenWolfScript), world, targetDistance: 1.5f);

        Assert.Equal([302u], fight.Rotation());
    }
}
