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

            var ability = Substitute.For<IAbility>();
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
    /// Only the instance the wounded character is in sends the hit, so a player in one instance never
    /// sees damage numbers from a fight in another.
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

            owning.CombatService.ApplyDamage(wounded, wounded, 10);

            // The wounded player is told directly and the hit is broadcast to the instance. Both
            // belong to the owning instance.
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
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new PassThroughCryptoSession());
        instance.AddCharacter(connection);
        return connection;
    }

    /// <summary>
    /// Disposing one instance must not silence the others: the survivor still handles a kill in it.
    /// </summary>
    [Fact]
    public void Leave_Other_Instances_Working_When_One_Is_Disposed()
    {
        MapInstance disposed = BuildInstance();
        MapInstance survivor = BuildInstance();

        try
        {
            var creature = new Creature
            {
                Guid = new ObjectGuid(ObjectType.Creature, 991_201),
                // Non-nullable on ICreature, and the death path reads BodyRemoveTimer off it to schedule
                // corpse removal, so a creature without metadata is not a valid one to kill.
                Metadata = Substitute.For<ICreatureMetadata>(),
                Health = 10,
                CurrentHealth = 10,
            };
            survivor.AddCreature(creature);
            creature.Script = new CreatureCombatScript(NullLoggerFactory.Instance, creature, survivor);

            disposed.Dispose();

            // The survivor owns this creature, so its kill handling must still run and clear the script.
            survivor.CombatService.ApplyDamage(creature, creature, 10);

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
