using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Enums;
using Avalon.World.Serialization;
using BenchmarkDotNet.Attributes;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// #980: one town broadcast's state updates, every player moving and seeing every other: each recipient's packet encoded
/// whole from its list (the per-recipient encoding), against one put together from entries encoded once per broadcast
/// (<see cref="StateUpdateAssembler" />). Encoding only: the entities are already described, and a motion-only update
/// has no entry for its own client. One operation is one broadcast to every player.
/// </summary>
[MemoryDiagnoser]
public class StateBroadcastEncodingBenchmarks
{
    private const GameEntityFields Moved = GameEntityFields.Position | GameEntityFields.Velocity | GameEntityFields.Orientation;

    private readonly PacketEncoder _encoder = new(new PayloadSegmentPool());
    private readonly StateUpdateAssembler _assembler = new();
    private readonly List<ObjectState> _list = new(64);
    private ObjectGuid[] _guids = null!;
    private ObjectState[] _states = null!;

    [Params(10, 25, 30)]
    public int Players { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _guids = new ObjectGuid[Players];
        _states = new ObjectState[Players];
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
        }
    }

    [Benchmark(Baseline = true, Description = "Each recipient's update encoded whole")]
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

    [Benchmark(Description = "Put together from entries encoded once per broadcast")]
    public int Shared()
    {
        int bytes = 0;
        _assembler.BeginBroadcast();
        for (int recipient = 0; recipient < Players; recipient++)
        {
            _assembler.BeginPacket();
            for (int entity = 0; entity < Players; entity++)
            {
                if (entity != recipient && !_assembler.TryAppendShared(_guids[entity], Moved))
                    _assembler.AppendShared(_guids[entity], Moved, _states[entity]);
            }

            OutboundPacket packet = _assembler.Finish(_encoder);
            bytes += packet.PayloadLength;
            packet.Release();
        }

        return bytes;
    }
}
