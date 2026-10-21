using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.World;

public enum MapTransitionResult : byte
{
    Success = 0,
    MapNotFound = 1,
    NotNearPortal = 2,
    LevelTooLow = 3,
    LevelTooHigh = 4,
    GenerationFailed = 5,
    /// <summary>The party's instance already holds as many players as it may (2026-09-30).</summary>
    InstanceFull = 6,
    /// <summary>A teleport's position had no walkable navmesh ground within its search box (item use).</summary>
    NoWalkableGround = 7,
    /// <summary>Another move to a map is already under way (a scroll return, an item teleport, a respawn or a party return).</summary>
    MoveInProgress = 8,
    /// <summary>The picked instance is not one the character may enter (#1104); it says nothing about whether it exists.</summary>
    InstanceUnavailable = 9,
    /// <summary>A new instance was asked for before the character's creation cooldown ran out (#1104).</summary>
    CreateOnCooldown = 10,
    /// <summary>A new party instance was asked for by a member who is not the party's leader (#1104).</summary>
    NotPartyLeader = 11,
    /// <summary>A new party instance was asked for by a character in no party (#1104).</summary>
    NotInParty = 12,
    /// <summary>The target map is a town, which has no instances to list or choose (#1104).</summary>
    NotInstanced = 13,
    /// <summary>The character is stunned (#770): it enters no portal until the stun ends.</summary>
    Stunned = 14,
}

[ProtoContract]
public class SMapTransitionPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_MAP_TRANSITION;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public MapTransitionResult Result { get; set; }
    [ProtoMember(2)] public Guid InstanceId { get; set; }
    [ProtoMember(3)] public ushort MapId { get; set; }
    [ProtoMember(4)] public float SpawnX { get; set; }
    [ProtoMember(5)] public float SpawnY { get; set; }
    [ProtoMember(6)] public float SpawnZ { get; set; }
    [ProtoMember(7)] public string MapName { get; set; } = string.Empty;
    [ProtoMember(8)] public string MapDescription { get; set; } = string.Empty;

    public static OutboundPacket Create(
        MapTransitionResult result,
        Guid instanceId,
        ushort mapId,
        float spawnX,
        float spawnY,
        float spawnZ,
        string mapName,
        string mapDescription,
        PacketEncoder encoder)
    {
        SMapTransitionPacket message = PacketEncoder.Scratch<SMapTransitionPacket>();
        message.Result = result;
        message.InstanceId = instanceId;
        message.MapId = mapId;
        message.SpawnX = spawnX;
        message.SpawnY = spawnY;
        message.SpawnZ = spawnZ;
        message.MapName = mapName;
        message.MapDescription = mapDescription;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }

    /// <summary>Creates a failure response with only the result code populated.</summary>
    public static OutboundPacket CreateFailure(MapTransitionResult result, PacketEncoder encoder) =>
        Create(result, Guid.Empty, 0, 0f, 0f, 0f, string.Empty, string.Empty, encoder);
}
