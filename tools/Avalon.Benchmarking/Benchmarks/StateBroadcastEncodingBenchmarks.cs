using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;
using Avalon.World.Serialization;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// #980: one broadcast's state packets in an instance where every player sees every other player and the same fifteen
/// creatures: a solo player or a party in a forest (1 to 3 players), a town (10 to 30). Each recipient's packet encoded
/// whole from its list (<c>Before</c>, the encoding before #980), put together from entries encoded once per broadcast
/// (<c>Shared</c>, <see cref="StatePacketAssembler" />), and what the instance does now (<c>After</c>: shared from
/// <see cref="StatePacketAssembler.MinRecipientsToShare" /> recipients, whole below). The updates are a broadcast with
/// every player and creature moving (a motion-only update has no entry for its own client); the adds are everyone
/// listed everything in full, each player its own character without its motion. Encoding only: the entities are
/// already described. One operation is one broadcast to every player.
/// </summary>
[MemoryDiagnoser]
public class StateBroadcastEncodingBenchmarks
{
    private const int Creatures = 15;

    // A world up for some two and a half years at 60 Hz (#1066): the state packets' tick a 5-byte varint, as a long-running
    // world sends it, not tick 0, which protobuf-net leaves off the wire.
    private const long LongUptime = 5_000_000_000;
    private const GameEntityFields Moved = GameEntityFields.Position | GameEntityFields.Velocity | GameEntityFields.Orientation;

    private readonly PacketEncoder _encoder = new(new PayloadSegmentPool());
    private readonly StatePacketAssembler _updates = StatePacketAssembler.ForUpdates();
    private readonly StatePacketAssembler _adds = StatePacketAssembler.ForAdds();
    private readonly List<ObjectState> _list = new(64);
    private ObjectGuid[] _guids = null!;
    private ObjectState[] _moved = null!;
    private ObjectState[] _full = null!;
    private ObjectState[] _own = null!;

    [Params(1, 2, 3, 10, 25, 30)]
    public int Players { get; set; }

    private int Entities => Players + Creatures;

    [GlobalSetup]
    public void Setup()
    {
        _guids = new ObjectGuid[Entities];
        _moved = new ObjectState[Entities];
        _full = new ObjectState[Entities];
        _own = new ObjectState[Players];
        for (int i = 0; i < Entities; i++)
        {
            bool player = i < Players;
            _guids[i] = new ObjectGuid(player ? ObjectType.Character : ObjectType.Creature, (uint)(1_000 + i));
            _moved[i] = new ObjectState
            {
                Guid = _guids[i].RawValue,
                Position = new Vec3 { X = 100.25f + i, Y = 0.5f, Z = -42.75f - i },
                Velocity = new Vec3 { X = 3.5f, Y = 0f, Z = -3.5f },
                Orientation = 135f,
                Name = player ? $"Player{i:D4}" : "Forest Wolf",
                Class = player ? CharacterClass.Warrior : null,
                Gender = player ? CharacterGender.Female : null,
                CreatureMetadataId = player ? null : 7UL,
            };
            _full[i] = Full(_moved[i], motion: true);
            if (player)
                _own[i] = Full(_moved[i], motion: false);
        }
    }

    [Benchmark(Baseline = true, Description = "Updates: Before")]
    public int UpdatesBefore() => UpdatesWhole();

    [Benchmark(Description = "Updates: Shared")]
    public int UpdatesShared() => UpdatesAssembled();

    [Benchmark(Description = "Updates: After")]
    public int UpdatesAfter() => Players >= StatePacketAssembler.MinRecipientsToShare ? UpdatesAssembled() : UpdatesWhole();

    [Benchmark(Description = "Adds: Before")]
    public int AddsBefore() => AddsWhole();

    [Benchmark(Description = "Adds: Shared")]
    public int AddsShared() => AddsAssembled();

    [Benchmark(Description = "Adds: After")]
    public int AddsAfter() => Players >= StatePacketAssembler.MinRecipientsToShare ? AddsAssembled() : AddsWhole();

    private int UpdatesWhole()
    {
        int bytes = 0;
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _list.Clear();
            for (int entity = 0; entity < Entities; entity++)
            {
                if (entity != recipient)
                    _list.Add(_moved[entity]);
            }

            OutboundPacket packet = SInstanceStateUpdatePacket.Create(_list, LongUptime, _encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }

    private int UpdatesAssembled()
    {
        int bytes = 0;
        _updates.BeginBroadcast(LongUptime);
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _updates.BeginPacket();
            for (int entity = 0; entity < Entities; entity++)
            {
                if (entity != recipient && !_updates.TryAppendShared(_guids[entity], Moved))
                    _updates.AppendShared(_guids[entity], Moved, _moved[entity]);
            }

            OutboundPacket packet = _updates.Finish(_encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }

    private int AddsWhole()
    {
        int bytes = 0;
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _list.Clear();
            for (int entity = 0; entity < Entities; entity++)
                _list.Add(entity == recipient ? _own[entity] : _full[entity]);

            OutboundPacket packet = SInstanceStateAddPacket.Create(_list, LongUptime, _encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }

    private int AddsAssembled()
    {
        int bytes = 0;
        _adds.BeginBroadcast(LongUptime);
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _adds.BeginPacket();
            for (int entity = 0; entity < Entities; entity++)
            {
                if (entity == recipient)
                    _adds.AppendOwn(_own[entity]);
                else if (!_adds.TryAppendShared(_guids[entity], GameEntityFields.All))
                    _adds.AppendShared(_guids[entity], GameEntityFields.All, _full[entity]);
            }

            OutboundPacket packet = _adds.Finish(_encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }

    /// <summary>An entity's full state, as an add describes it; without its motion for a character's own client.</summary>
    private static ObjectState Full(ObjectState moved, bool motion) => new()
    {
        Guid = moved.Guid,
        Position = motion ? moved.Position : null,
        Velocity = motion ? moved.Velocity : null,
        Orientation = motion ? moved.Orientation : null,
        MoveState = MoveState.Running,
        Health = 1_200,
        CurrentHealth = 1_150,
        PowerType = PowerType.Mana,
        Power = 100,
        CurrentPower = 35,
        Level = 12,
        IsDead = false,
        Name = moved.Name,
        Class = moved.Class,
        Gender = moved.Gender,
        CreatureMetadataId = moved.CreatureMetadataId,
    };
}
