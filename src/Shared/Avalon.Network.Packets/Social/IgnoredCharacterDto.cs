using ProtoBuf;

namespace Avalon.Network.Packets.Social;

/// <summary>One character on the recipient's ignore list (#723).</summary>
[ProtoContract]
public class IgnoredCharacterDto
{
    [ProtoMember(1)] public uint CharacterId { get; set; }

    /// <summary>The name the character had when the list was loaded or the entry added.</summary>
    [ProtoMember(2)] public string Name { get; set; } = string.Empty;
}
