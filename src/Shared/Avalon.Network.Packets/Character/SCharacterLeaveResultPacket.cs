using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>The answer to a CCharacterLeavePacket. Append-only.</summary>
public enum CharacterLeaveResult : byte
{
    /// <summary>What a payload without the field decodes as; never sent.</summary>
    Unknown = 0,

    /// <summary>
    /// The character left its instance and its logout save committed. The connection holds no
    /// character, and CMSG_CHARACTER_LIST and CMSG_CHARACTER_SELECTED are accepted again.
    /// </summary>
    Left = 1,

    /// <summary>The connection holds no character. Nothing changed.</summary>
    NoCharacter = 2,

    /// <summary>
    /// A character select is still under way, or the selected character is waiting on the client's
    /// load report. Nothing changed; leave once the character is in the world.
    /// </summary>
    Selecting = 3,

    /// <summary>
    /// A leave is already under way; the first one's answer is still to come. Nothing else changed,
    /// and the logout save runs once.
    /// </summary>
    AlreadyLeaving = 4,
}

/// <summary>
/// The answer to one CCharacterLeavePacket, sent to the requester only (#663). A leave whose logout
/// save fails is never answered: the connection is closed with
/// <c>DisconnectReason.CharacterSaveFailed</c> instead.
/// </summary>
[ProtoContract]
public class SCharacterLeaveResultPacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_CHARACTER_LEAVE_RESULT;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public CharacterLeaveResult Result { get; set; }

    public static NetworkPacket Create(CharacterLeaveResult result, EncryptFunc encrypt)
        => PacketSerializationHelper.Serialize(
            new SCharacterLeaveResultPacket { Result = result },
            PacketType, Flags, Protocol, encrypt);
}
