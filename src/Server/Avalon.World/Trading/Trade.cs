using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;

namespace Avalon.World.Trading;

/// <summary>Where a trade stands.</summary>
public enum TradeState : byte
{
    /// <summary>Both sides may offer, take back, set gold, confirm and cancel.</summary>
    Open,

    /// <summary>Both confirmed and the paired save is in flight: nothing about the trade can change until it ends.</summary>
    Completing,
}

/// <summary>One player's side of a trade (#1175). World-side, tick thread only.</summary>
public sealed class TradeSide(IWorldConnection connection, CharacterEntity character)
{
    public IWorldConnection Connection { get; } = connection;

    public CharacterEntity Character { get; } = character;

    /// <summary>
    /// By trade slot, each item as it was when offered (its Slot is the Bag slot it stays in, locked); null where the
    /// slot is empty.
    /// </summary>
    public InventoryItem?[] Items { get; } = new InventoryItem?[TradeRules.MaxItems];

    /// <summary>The gold offered, held in the character's balance.</summary>
    public ulong Gold { get; internal set; }

    /// <summary>Whether this side confirmed the trade's current revision.</summary>
    public bool Confirmed { get; internal set; }

    /// <summary>True until the character leaves the world while the trade completes.</summary>
    public bool Online { get; internal set; } = true;

    /// <summary>This side as the completion rules read it: the offered items in trade-slot order, and the gold.</summary>
    public TradeOffer Offer()
    {
        var items = new List<InventoryItem>(TradeRules.MaxItems);
        for (int i = 0; i < Items.Length; i++)
        {
            if (Items[i] is { } item)
                items.Add(item);
        }

        return new TradeOffer(Character, items, Gold);
    }
}

/// <summary>
/// An open trade between two players (#1175): the sender of the accepted request is <see cref="First" />. World-side and
/// in memory only, tick thread only; <see cref="TradeService" /> owns every change.
/// </summary>
public sealed class Trade(uint id, TradeSide first, TradeSide second)
{
    /// <summary>Numbered from 1 for each run of the server.</summary>
    public uint Id { get; } = id;

    public TradeSide First { get; } = first;

    public TradeSide Second { get; } = second;

    public TradeState State { get; internal set; } = TradeState.Open;

    /// <summary>
    /// 1 when opened, and one more for every change to either offer: a confirm names the revision it approves.
    /// </summary>
    public uint Revision { get; internal set; } = 1;

    /// <summary>The id of the trade's record, set when completion starts.</summary>
    public Guid RecordId { get; internal set; }

    /// <summary>The side of this character, which must be one of the two.</summary>
    public TradeSide SideOf(uint characterId) => First.Character.Guid.Id == characterId ? First : Second;

    /// <summary>The other side from this character, which must be one of the two.</summary>
    public TradeSide PartnerOf(uint characterId) => First.Character.Guid.Id == characterId ? Second : First;
}
