using Avalon.Common;
using Avalon.Common.Cryptography;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.ChunkLayouts;
using Avalon.Domain.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Instances;

public class MapInstanceDisposalShould
{
    /// <summary>
    /// The leak this type's disposal exists to prevent. <c>MapInstance</c>'s constructor subscribes to
    /// static events on <see cref="Creature" /> and <see cref="CharacterEntity" />, and a static event's
    /// delegate holds a strong reference to its target — so an instance dropped by the registry stayed
    /// a GC root and could never be collected, taking its navigator, chunk layout, combat services and
    /// every entity dictionary with it. Measured before the fix: twenty instances created, twenty still
    /// reachable after a full collection.
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
    /// than on whether the static events were unsubscribed.
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
    /// Disposal has to actually detach, not merely mark the instance dead: a retired instance that
    /// still ran its handlers would keep mutating entities and broadcasting to connections it no
    /// longer owns.
    /// </summary>
    [Fact]
    public void Stop_Reacting_To_Entity_Events_Once_Disposed()
    {
        MapInstance instance = BuildInstance();

        var creature = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 991_001) };
        instance.AddCreature(creature);
        creature.Script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, instance);

        instance.Dispose();
        creature.Died(creature);

        Assert.NotNull(creature.Script);
    }

    /// <summary>
    /// <c>CharacterEntity.OnUnitDamaged</c> is a static event, so every live instance receives every
    /// hit in the process. Only the instance the wounded character actually belongs to may broadcast
    /// it — otherwise a player in one instance sees damage numbers from a fight in another. This was
    /// the one handler of the nine that did not filter.
    /// </summary>
    [Fact]
    public void Broadcast_A_Hit_Only_From_The_Instance_The_Character_Is_In()
    {
        MapInstance owning = BuildInstance();
        MapInstance bystander = BuildInstance();

        try
        {
            CharacterEntity wounded = NewCharacter(991_101);
            IWorldConnection ownConnection = SeatCharacter(owning, wounded);
            IWorldConnection otherConnection = SeatCharacter(bystander, NewCharacter(991_102));

            wounded.OnHit(wounded, 10);

            // Two sends, not one: OnCharacterSelfDamaged tells the wounded player directly and
            // OnCharacterHit broadcasts the hit to the instance. Both belong to the owning instance.
            ownConnection.ReceivedWithAnyArgs().Send(default!);
            otherConnection.DidNotReceiveWithAnyArgs().Send(default!);
        }
        finally
        {
            owning.Dispose();
            bystander.Dispose();
        }
    }

    /// <summary>
    /// A real <see cref="CharacterEntity" /> rather than a substitute, because the hit has to travel
    /// the production route — <c>OnHit</c> raising the static <c>OnUnitDamaged</c> — for the guard
    /// under test to be the thing that decides who broadcasts.
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
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new PassThroughCryptoSession());
        instance.AddCharacter(connection);
        return connection;
    }

    /// <summary>
    /// Disposing one instance must not silence the others. These events are static and shared, so
    /// <c>-=</c> is doing the load-bearing work here: it removes only the delegate whose target is the
    /// disposed instance, leaving every other subscriber's entry in the invocation list. Written
    /// because the obvious "simplification" of <see cref="MapInstance.Dispose" /> — assigning the
    /// event to <c>null</c>, or clearing it — would compile, would pass every other test in this file,
    /// and would stop every surviving instance in the process from ever reacting to an entity again.
    /// </summary>
    [Fact]
    public void Leave_Other_Instances_Subscribed_When_One_Is_Disposed()
    {
        MapInstance disposed = BuildInstance();
        MapInstance survivor = BuildInstance();

        try
        {
            var creature = new Creature { Guid = new ObjectGuid(ObjectType.Creature, 991_201) };
            survivor.AddCreature(creature);
            creature.Script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, survivor);

            disposed.Dispose();

            // The survivor owns this creature, so its OnCreatureKilled must still run and clear the
            // script. If Dispose cleared the shared event rather than removing one delegate, nothing
            // would run and the script would still be set.
            creature.Died(creature);

            Assert.Null(creature.Script);
        }
        finally
        {
            survivor.Dispose();
        }
    }

    private static MapInstance BuildInstance()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var world = Substitute.For<Avalon.World.IWorld>();
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

/// <summary>
/// NSubstitute cannot proxy a method taking <see cref="ReadOnlySpan{T}" />, and the broadcast paths
/// exercised here run packets through <c>Encrypt</c> — a substitute produces a proxy that throws
/// <see cref="InvalidProgramException" /> at the call. Mirrors the Auth suite's fake of the same name.
/// </summary>
internal sealed class PassThroughCryptoSession : IAvalonCryptoSession
{
    public void Initialize(byte[] otherEndPublicKeyBytes) { }
    public byte[] GetPublicKey() => Array.Empty<byte>();
    public byte[] GetOtherEndPublicKey() => Array.Empty<byte>();
    public byte[] Encrypt(ReadOnlySpan<byte> data) => data.ToArray();

    public int Decrypt(ReadOnlySpan<byte> data, byte[] output)
    {
        data.CopyTo(output);
        return data.Length;
    }

    public byte[] GenerateHandshakeData() => Array.Empty<byte>();
}
