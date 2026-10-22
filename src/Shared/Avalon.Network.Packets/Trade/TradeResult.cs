namespace Avalon.Network.Packets.Trade;

/// <summary>
/// The answer to one trade request, or why a trade or a request ended, always as the player it is sent to sees it.
/// Append-only; 0 is what a payload without the field reads as.
/// </summary>
public enum TradeResult : byte
{
    /// <summary>No result: what a payload without the field reads as.</summary>
    Unknown = 0,

    /// <summary>The request was applied, or the trade completed.</summary>
    Ok = 1,

    /// <summary>The requested player is not online in the same map instance.</summary>
    NotFound = 2,

    /// <summary>A player cannot trade with themselves.</summary>
    Self = 3,

    /// <summary>The other player is beyond the request range (5 m), or beyond the keep range (6 m) for an accept.</summary>
    TooFar = 4,

    /// <summary>The other player is already trading or holds an open request.</summary>
    TargetBusy = 5,

    /// <summary>This player is already trading or holds an open request.</summary>
    Busy = 6,

    /// <summary>This player is in combat.</summary>
    InCombat = 7,

    /// <summary>The other player is in combat.</summary>
    TargetInCombat = 8,

    /// <summary>This player is dead.</summary>
    Dead = 9,

    /// <summary>The other player is dead.</summary>
    TargetDead = 10,

    /// <summary>This player has a shop, bank or other NPC window open.</summary>
    NpcWindowOpen = 11,

    /// <summary>Too many trade requests too quickly: the sender's request budget is spent.</summary>
    TooFast = 12,

    /// <summary>An accept or decline with no open request from that player.</summary>
    NoRequest = 13,

    /// <summary>The other player declined the request.</summary>
    Declined = 14,

    /// <summary>The request was not answered in time (30 s).</summary>
    Expired = 15,

    /// <summary>A trade action from a player with no open trade.</summary>
    NotTrading = 16,

    /// <summary>A trade slot or Bag slot outside its range.</summary>
    InvalidSlot = 17,

    /// <summary>The named Bag slot holds no item.</summary>
    ItemNotFound = 18,

    /// <summary>The item is NoTrade, a quest item, or its template no longer exists.</summary>
    NotTradeable = 19,

    /// <summary>The item is already in the trade.</summary>
    AlreadyOffered = 20,

    /// <summary>The trade slot already holds an item.</summary>
    TradeSlotTaken = 21,

    /// <summary>The gold offered is more than this player's balance.</summary>
    NotEnoughGold = 22,

    /// <summary>A confirm for an offer that has changed since the player saw it.</summary>
    StaleOffer = 23,

    /// <summary>Both confirmed and the trade is being saved: nothing about it can change until the save ends.</summary>
    Completing = 24,

    /// <summary>This player cancelled the trade.</summary>
    Cancelled = 25,

    /// <summary>The other player cancelled the trade.</summary>
    CancelledByPartner = 26,

    /// <summary>The two players moved beyond the keep range (6 m) of each other.</summary>
    OutOfRange = 27,

    /// <summary>A player entered combat, which ends the trade.</summary>
    EnteredCombat = 28,

    /// <summary>A player died, which ends the trade.</summary>
    Died = 29,

    /// <summary>A player left the map, the instance or the world, which ends the trade.</summary>
    Left = 30,

    /// <summary>A player opened a shop, bank or other NPC window, which ends the trade.</summary>
    NpcWindowOpened = 31,

    /// <summary>This player's Bag would not have room for everything received, once its own offered items leave.</summary>
    BagFull = 32,

    /// <summary>The other player's Bag would not have room for everything they receive.</summary>
    PartnerBagFull = 33,

    /// <summary>The trade would take this player's balance past the gold cap.</summary>
    GoldCapReached = 34,

    /// <summary>The trade would take the other player's balance past the gold cap.</summary>
    PartnerGoldCapReached = 35,

    /// <summary>This player would own two copies of a Unique item.</summary>
    UniqueAlreadyOwned = 36,

    /// <summary>The other player would own two copies of a Unique item.</summary>
    PartnerUniqueAlreadyOwned = 37,

    /// <summary>An offered item is no longer where it was offered or no longer tradeable, or a giver lacks the gold offered.</summary>
    OfferInvalid = 38,

    /// <summary>The trade's save was refused or failed: nothing moved.</summary>
    SaveFailed = 39,

    /// <summary>The server failed while handling the request; nothing is known to have changed.</summary>
    Error = 40,

    /// <summary>The other player has a shop, bank or other NPC window open: an accept is refused until it closes.</summary>
    TargetNpcWindowOpen = 41,
}
