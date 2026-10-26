using Avalon.Common;
using ProtoBuf;

namespace Avalon.Network.Packets.Items;

/// <summary>
/// The world's item prices the client shows before it asks: what the Resonance Workbench charges and what an
/// unidentified item sells for. Sent with the character select, from the world's Game:Items configuration.
/// </summary>
[ProtoContract]
public class ItemPricesDto
{
    /// <summary>Game:Items:IdentifyCost: the gold identifying one item costs; 0 is free.</summary>
    [ProtoMember(1)] public ulong IdentifyCost { get; set; }

    /// <summary>Game:Items:ImprintCost: the gold imprinting a glyph costs, by its modifier's tier, sorted by tier.</summary>
    [ProtoMember(2)] public List<ImprintCostDto> ImprintCosts { get; set; } = [];

    /// <summary>Game:Items:UnidentifiedSellPercent: what an unidentified item sells for, in percent of its price.</summary>
    [ProtoMember(3)] public uint UnidentifiedSellPercent { get; set; }

    /// <summary>Game:Items:SellFactor: what a sale pays per rarity, as a multiple of the template's sell price.</summary>
    [ProtoMember(4)] public List<SellFactorDto> SellFactors { get; set; } = [];
}

/// <summary>One rarity's sell factor.</summary>
[ProtoContract]
public class SellFactorDto
{
    [ProtoMember(1)] public ItemRarity Rarity { get; set; }

    [ProtoMember(2)] public uint Factor { get; set; }
}

/// <summary>One imprint tier's price.</summary>
[ProtoContract]
public class ImprintCostDto
{
    /// <summary>The glyph modifier's tier rank, 9 the weakest.</summary>
    [ProtoMember(1)] public uint Tier { get; set; }

    [ProtoMember(2)] public ulong Gold { get; set; }
}
