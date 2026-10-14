using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;
using Avalon.World.Serialization;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// #980: one town broadcast's state packets, every player seeing every other: each recipient's packet encoded whole from
/// its list (the per-recipient encoding), against one put together from entries encoded once per broadcast
/// (<see cref="StatePacketAssembler" />). The updates are a broadcast with every player moving (a motion-only update has
/// no entry for its own client); the adds are the town's first tick, everyone listed to everyone in full, each player
/// its own character without its motion. Encoding only: the entities are already described. One operation is one
/// broadcast to every player.
/// </summary>
[MemoryDiagnoser]
public class StateBroadcastEncodingBenchmarks
{
    private const GameEntityFields Moved = GameEntityFields.Position | GameEntityFields.Velocity | GameEntityFields.Orientation;

    private readonly PacketEncoder _encoder = new(new PayloadSegmentPool());
    private readonly StatePacketAssembler _updates = StatePacketAssembler.ForUpdates();
    private readonly StatePacketAssembler _adds = StatePacketAssembler.ForAdds();
    private readonly List<ObjectState> _list = new(64);
    private ObjectGuid[] _guids = null!;
    private ObjectState[] _states = null!;
    private ObjectState[] _full = null!;
    private ObjectState[] _own = null!;

    [Params(10, 25, 30)]
    public int Players { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _guids = new ObjectGuid[Players];
        _states = new ObjectState[Players];
        _full = new ObjectState[Players];
        _own = new ObjectState[Players];
        for (int i = 0; i < Players; i++)
        {
            _guids[i] = new ObjectGuid(ObjectType.Character, (uint)(1_000 + i));
            _states[i] = new ObjectState
            {
                Guid = _guids[i].RawValue,
                Position = new Vec3 { X = 100.25f + i, Y = 0.5f, Z = -42.75f - i },
                Velocity = new Vec3 { X = 3.5f, Y = 0f, Z = -3.5f },
                Orientation = 135f,
                Name = $"Player{i:D4}",
                Class = CharacterClass.Warrior,
                Gender = CharacterGender.Female,
            };
            _full[i] = Full(_states[i], motion: true);
            _own[i] = Full(_states[i], motion: false);
        }
    }

    [Benchmark(Baseline = true, Description = "Updates: each recipient's encoded whole")]
    public int PerRecipient()
    {
        int bytes = 0;
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _list.Clear();
            for (int entity = 0; entity < Players; entity++)
            {
                if (entity != recipient)
                    _list.Add(_states[entity]);
            }

            OutboundPacket packet = SInstanceStateUpdatePacket.Create(_list, _encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }

    [Benchmark(Description = "Updates: put together from entries encoded once per broadcast")]
    public int Shared()
    {
        int bytes = 0;
        _updates.BeginBroadcast();
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _updates.BeginPacket();
            for (int entity = 0; entity < Players; entity++)
            {
                if (entity != recipient && !_updates.TryAppendShared(_guids[entity], Moved))
                    _updates.AppendShared(_guids[entity], Moved, _states[entity]);
            }

            OutboundPacket packet = _updates.Finish(_encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }

    [Benchmark(Description = "Adds: each recipient's encoded whole")]
    public int AddsPerRecipient()
    {
        int bytes = 0;
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _list.Clear();
            for (int entity = 0; entity < Players; entity++)
                _list.Add(entity == recipient ? _own[entity] : _full[entity]);

            OutboundPacket packet = SInstanceStateAddPacket.Create(_list, _encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }

    [Benchmark(Description = "Adds: put together from entries encoded once per broadcast")]
    public int AddsShared()
    {
        int bytes = 0;
        _adds.BeginBroadcast();
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _adds.BeginPacket();
            for (int entity = 0; entity < Players; entity++)
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

    /// <summary>A character's full state, as an add describes it; without its motion for its own client.</summary>
    private static ObjectState Full(ObjectState moved, bool motion) => new()
    {
        Guid = moved.Guid,
        Position = motion ? moved.Position : null,
        Velocity = motion ? moved.Velocity : null,
        Orientation = motion ? moved.Orientation : null,
        MoveState = MoveState.Running,
        Health = 1_200,
        CurrentHealth = 1_150,
        PowerType = PowerType.Fury,
        Power = 100,
        CurrentPower = 35,
        Level = 12,
        IsDead = false,
        Experience = 4_500,
        RequiredExperience = 9_000,
        Name = moved.Name,
        Class = moved.Class,
        Gender = moved.Gender,
    };
}
