using ProtoBuf;

namespace Avalon.Network.Packets.Items;

/// <summary>One modifier an item rolled (#1179): which catalog modifier it is and the numbers it rolled.</summary>
[ProtoContract]
public class ItemModifierDto
{
    /// <summary>The catalog modifier (ItemModifiers.Id in the world's reference data).</summary>
    [ProtoMember(1)] public uint ModifierId { get; set; }

    /// <summary>In the order of the catalog modifier's stat lines.</summary>
    [ProtoMember(2)] public List<ItemModifierValueDto> Values { get; set; } = [];
}

/// <summary>
/// One rolled number of a modifier: the stat it gives and how much. <see cref="Value2" /> is set exactly on an
/// "adds X to Y" line, as its high end.
/// </summary>
[ProtoContract]
public class ItemModifierValueDto
{
    [ProtoMember(1)] public ItemStat Stat { get; set; }

    [ProtoMember(2)] public int Value { get; set; }

    [ProtoMember(3)] public int? Value2 { get; set; }
}
