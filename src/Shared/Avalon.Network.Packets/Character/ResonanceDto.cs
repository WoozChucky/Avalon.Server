using ProtoBuf;

namespace Avalon.Network.Packets.Character;

/// <summary>
/// One Resonance theme on the character sheet (#1180): how many worn items carry at least one prefix or suffix of the
/// theme. <see cref="ThemeId" /> is the theme's id in the vendored modifier catalog, where the client finds its name.
/// </summary>
[ProtoContract]
public class ResonanceDto
{
    [ProtoMember(1)] public uint ThemeId { get; set; }

    /// <summary>The worn items carrying the theme, each counted once however many of its affixes share it; never 0.</summary>
    [ProtoMember(2)] public uint Count { get; set; }
}
