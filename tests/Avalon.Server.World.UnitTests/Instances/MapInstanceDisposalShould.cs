using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

public class MapInstanceDisposalShould
{
    /// <summary>
    /// The leak disposal once existed to prevent: <c>MapInstance</c> used to subscribe to static events
    /// on <see cref="Creature" /> and <see cref="CharacterEntity" />, and a static event's delegate holds
    /// a strong reference to its target, so an instance dropped by the registry stayed a GC root. There
    /// are no static events now (#546); this pins that nothing else roots a retired instance either.
    /// </summary>
    [Fact]
    public void Become_Collectable_Once_Disposed()
    {
        WeakReference weak = BuildAndAbandon();

        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive, "a disposed MapInstance is still reachable, so it still leaks");
    }

    /// <summary>
    /// Kept in its own non-inlined method so the instance has no live local slot in the caller's frame
    /// by the time the collection runs — otherwise the test could pass or fail on JIT liveness rather
    /// than on whether anything still holds the instance.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference BuildAndAbandon()
    {
        MapInstance instance = BuildInstance();
        var weak = new WeakReference(instance);
        instance.Dispose();
        return weak;
    }

    /// <summary>
    /// A retired instance hears nothing from a live one (#546): a hit, a kill and a cast broadcast in
    /// another instance neither touch its creature nor reach its connection. When the handlers were
    /// static events, a disposal that forgot to detach one left the retired instance reacting.
    /// </summary>
    [Fact]
    public void Receive_Nothing_From_Another_Instance_Once_Disposed()
    {
        MapInstance retired = BuildInstance();
        MapInstance live = BuildInstance();

        try
        {
            var retiredCreature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, 991_001),
                Metadata = Substitute.For<ICreatureMetadata>(),
            };
            retired.AddCreature(retiredCreature);
            retiredCreature.Script = new CreatureCombatScript(NullLoggerFactory.Instance, retiredCreature, retired);
            IWorldConnection retiredConnection = SeatCharacter(retired, NewCharacter(991_002));
            retired.Dispose();

            CharacterEntity wounded = NewCharacter(991_003);
            SeatCharacter(live, wounded);
            var liveCreature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, 991_004),
                Metadata = Substitute.For<ICreatureMetadata>(),
                Health = 10,
                CurrentHealth = 10,
            };
            live.AddCreature(liveCreature);
            liveCreature.Script = new CreatureCombatScript(NullLoggerFactory.Instance, liveCreature, live);

            IAbility ability = Substitute.For<IAbility>();
            ability.AbilityId.Returns(new AbilityId(1));

            live.CombatService.ApplyDamage(liveCreature, wounded, 5);
            live.CombatService.ApplyDamage(liveCreature, liveCreature, 10);
            live.BroadcastFinishCast(wounded, ability);

            Assert.Null(liveCreature.Script);
            Assert.NotNull(retiredCreature.Script);
            retiredConnection.DidNotReceiveWithAnyArgs().Send(default!);
        }
        finally
        {
            live.Dispose();
        }
    }

    /// <summary>
    /// A real <see cref="CharacterEntity" /> rather than a substitute, because the hit has to travel
    /// the production route — the combat service's hit on a real entity — to reach the instance.
    /// </summary>
    private static CharacterEntity NewCharacter(uint id)
    {
        var entity = new CharacterEntity(
            NullLoggerFactory.Instance,
            new Character { Id = id, Health = 100, Power1 = 0 },
            new RegenConfiguration());

        entity.Spells.Load(new List<IAbility>());
        entity.Guid = new ObjectGuid(ObjectType.Character, id);
        return entity;
    }

    private static IWorldConnection SeatCharacter(MapInstance instance, ICharacter character)
    {
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        instance.AddCharacter(connection);
        return connection;
    }

    private static MapInstance BuildInstance()
    {
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        IWorld world = Substitute.For<Avalon.World.IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(
            Seed: 0,
            Chunks: new[] { entryChunk },
            EntryChunk: entryChunk,
            BossChunk: null,
            Portals: Array.Empty<PortalPlacement>(),
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: null);

        return new MapInstance(
            NullLoggerFactory.Instance,
            serviceProvider,
            world,
            new MapTemplateId(1),
            ownerCharacterId: null,
            layout,
            Substitute.For<IMapNavigator>(),
            seed: 0);
    }
}
