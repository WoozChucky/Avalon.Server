namespace Avalon.Network.Packets.World;

/// <summary>
/// Which instance a <see cref="CEnterMapPacket" /> asks to enter (#1104).
/// </summary>
/// <remarks>
/// Values are only ever appended. A choice other than <see cref="Default" /> is refused for a town target with
/// <see cref="MapTransitionResult.NotInstanced" />.
/// </remarks>
public enum MapEntryChoice : byte
{
    /// <summary>The default instance, or a new one when none is live. Also what a client without the field sends.</summary>
    Default = 0,

    /// <summary>The live instance the packet's InstanceId names, from an SMSG_INSTANCE_LIST answer.</summary>
    Existing = 1,

    /// <summary>A new private instance, which becomes the character's default.</summary>
    NewPrivate = 2,

    /// <summary>A new party instance, which becomes the party's default; the party's leader only.</summary>
    NewParty = 3,
}
