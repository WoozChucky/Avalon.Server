using ProtoBuf;

namespace Avalon.Network.Packets.State;

[ProtoContract]
public class CreatureAdd
{
    [ProtoMember(1)] public ulong Id { get; set; }
    [ProtoMember(2)] public ulong TemplateId { get; set; }
    [ProtoMember(3)] public string Name { get; set; }
    [ProtoMember(4)] public uint Health { get; set; }
    [ProtoMember(5)] public uint? Power { get; set; }
    [ProtoMember(6)] public ushort Level { get; set; }
    [ProtoMember(7)] public float PositionX { get; set; }
    [ProtoMember(8)] public float PositionY { get; set; }
    [ProtoMember(9)] public float PositionZ { get; set; }
    [ProtoMember(10)] public float VelocityX { get; set; }
    [ProtoMember(11)] public float VelocityY { get; set; }
    [ProtoMember(12)] public float VelocityZ { get; set; }
    [ProtoMember(13)] public float Orientation { get; set; }
    [ProtoMember(14)] public MoveState MoveState { get; set; }
}

[ProtoContract]
public class CharacterAdd
{
    [ProtoMember(1)] public ulong Id { get; set; }
    [ProtoMember(2)] public string Name { get; set; }
    [ProtoMember(3)] public uint Health { get; set; }
    [ProtoMember(4)] public uint? Power { get; set; }
    [ProtoMember(5)] public ushort Level { get; set; }
    [ProtoMember(6)] public float PositionX { get; set; }
    [ProtoMember(7)] public float PositionY { get; set; }
    [ProtoMember(8)] public float PositionZ { get; set; }
    [ProtoMember(9)] public float VelocityX { get; set; }
    [ProtoMember(10)] public float VelocityY { get; set; }
    [ProtoMember(11)] public float VelocityZ { get; set; }
    [ProtoMember(12)] public float Orientation { get; set; }
    [ProtoMember(13)] public MoveState MoveState { get; set; }
}

public enum MoveState
{
    Idle,
    Walking,
    Running,
}
