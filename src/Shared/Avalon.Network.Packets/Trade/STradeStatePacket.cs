using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Items;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Network.Packets.Trade;

/// <summary>One offered item, as both players see it.</summary>
[ProtoContract]
public class TradeItemDto
{
    /// <summary>The trade slot, 0 to 14.</summary>
    [ProtoMember(1)] public byte TradeSlot { get; set; }
    [ProtoMember(2)] public ulong ItemTemplateId { get; set; }
    [ProtoMember(3)] public Guid ItemInstanceId { get; set; }

    /// <summary>The whole stack is offered.</summary>
    [ProtoMember(4)] public uint Count { get; set; }

    /// <summary>The copy's rarity (#1179).</summary>
    [ProtoMember(5)] public ItemRarity Rarity { get; set; }

    /// <summary>The copy's item level (#1179).</summary>
    [ProtoMember(6)] public uint ItemLevel { get; set; }

    /// <summary>
    /// The modifiers the copy rolled (#1179), in the order it shows them; null for an item without any, which is what an
    /// empty repeated field reads back as.
    /// </summary>
    [ProtoMember(7)] public List<ItemModifierDto>? Modifiers { get; set; }

    /// <summary>
    /// The copy's name (#1179): set for a magic copy (its prefix and suffix around the template's name) and a rare one
    /// (the two words its roll gave it); null where the template's name is the item's.
    /// </summary>
    [ProtoMember(8)] public string? Name { get; set; }
}

/// <summary>One side of a trade: its items in trade-slot order, its gold, and whether it confirmed this revision.</summary>
[ProtoContract]
public class TradeSideDto
{
    [ProtoMember(1)] public ulong Gold { get; set; }
    [ProtoMember(2)] public bool Confirmed { get; set; }
    [ProtoMember(3)] public List<TradeItemDto> Items { get; set; } = [];
}

/// <summary>
/// The whole trade as the recipient sees it, sent to both players on every change, so each client simply redraws. A
/// confirm names the revision it approves, and one for an older revision is refused with
/// <see cref="TradeResult.StaleOffer" />. While <see cref="Completing" /> the trade is being saved, and nothing about it
/// can change.
/// </summary>
[ProtoContract]
public class STradeStatePacket : Packet
{
    public static NetworkPacketType PacketType = NetworkPacketType.SMSG_TRADE_STATE;
    public static NetworkProtocol Protocol = NetworkProtocol.Tcp;
    public static NetworkPacketFlags Flags = NetworkPacketFlags.Encrypted;

    [ProtoMember(1)] public uint TradeId { get; set; }

    /// <summary>1 when the trade opens, and one more for every change to either offer.</summary>
    [ProtoMember(2)] public uint Revision { get; set; }

    [ProtoMember(3)] public bool Completing { get; set; }
    // Never null, so a reader always has both sides; the server assigns both on every state, so it always writes them.
#pragma warning disable PBN0022
    [ProtoMember(4)] public TradeSideDto You { get; set; } = new();
    [ProtoMember(5)] public TradeSideDto Partner { get; set; } = new();
#pragma warning restore PBN0022

    /// <remarks>
    /// Both sides are the caller's, assigned whole and encoded before this returns, so a server may fill the same two
    /// sides for every state it sends.
    /// </remarks>
    public static OutboundPacket Create(uint tradeId, uint revision, bool completing, TradeSideDto you, TradeSideDto partner,
        PacketEncoder encoder)
    {
        STradeStatePacket message = PacketEncoder.Scratch<STradeStatePacket>();
        message.TradeId = tradeId;
        message.Revision = revision;
        message.Completing = completing;
        message.You = you;
        message.Partner = partner;
        return encoder.Encode(message, PacketType, Flags, Protocol);
    }
}
