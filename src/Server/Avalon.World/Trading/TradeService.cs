using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Items;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Trade;
using Avalon.World.Characters;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Social;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Trading;

/// <summary>
/// Every trade request and open trade on this world server (#1175), in memory only. Tick thread only, every method: the
/// handlers, World's transfer, despawn and NPC window paths, and World.Update all call it on the tick, and the mutators
/// assert the tick thread while <see cref="TickThreadGuard" /> is enabled. Deadlines are read from the container's
/// TimeProvider inside those calls; there are no timers.
/// A character holds at most one request, sent or received, and is in at most one trade. A request to a character who
/// ignores the sender is answered as a sent one and never reaches the target: it occupies the sender's request only,
/// and expires for the sender alone, so nothing discloses the ignore.
/// Every change to either offer clears both confirmations, moves the revision on and sends the whole trade to both; a
/// change that changes nothing sends nothing and clears nothing. Closing a trade, for any reason, releases every lock
/// on both sides.
/// </summary>
public sealed partial class TradeService(IOptions<GameConfiguration> options, TimeProvider time, ICharacterSaver saver,
    OnlineCharacters online, ILogger<TradeService> logger, TickThreadGuard? tickThread = null)
{
    // Requests by character: a visible one under both its sender and its target, a hidden one (its target ignores the
    // sender) under its sender only. _requests holds every one, for the expiry sweep.
    private readonly Dictionary<uint, TradeRequest> _requestOf = [];
    private readonly List<TradeRequest> _requests = [];

    // Trades by both characters, and every trade in a list for the sweep, iterated by index so no enumerator is made.
    private readonly Dictionary<uint, Trade> _tradeOf = [];
    private readonly List<Trade> _trades = [];

    // The request budget per sender, a limit on request spam: every request that looks its target up spends one.
    // Forgotten when the sender goes offline, as the party invite budget is.
    private readonly CharacterRateBudget _requestBudget = new(() => options.Value.TradeRequestsPerMinute, time);

    // The two sides of a state packet, filled once for each state and handed to both recipients' packets, each of which
    // is encoded before STradeStatePacket.Create returns and keeps nothing of them: two sides, two item lists and one
    // entry per trade slot of each side, reused by every state (as the party roster's entries are, #973).
    private readonly TradeSideDto[] _sideDtos = [new(), new()];
    private readonly List<TradeItemDto>[] _itemLists =
        [new List<TradeItemDto>(TradeRules.MaxItems), new List<TradeItemDto>(TradeRules.MaxItems)];
    private readonly TradeItemDto[] _itemDtos = NewItemDtos();

    // The completion's (TradeService.Completion.cs): the paired save, and the economy the swap goes through.
    private readonly ICharacterSaver _saver = saver;
    private ICharacterEconomy? _economy;
    private Func<ItemTemplateId, ItemTemplate?> _findTemplate = static _ => null;
    private uint _lastTradeId;

    private GameConfiguration Config => options.Value;

    /// <summary>Who is online, shared with the party service and the whisper.</summary>
    public OnlineCharacters Online => online;

    /// <summary>World.LoadAsync calls this once its economy and item templates exist.</summary>
    public void Attach(ICharacterEconomy economy, Func<ItemTemplateId, ItemTemplate?> findTemplate)
    {
        _economy = economy;
        _findTemplate = findTemplate;
    }

    /// <summary>The trade this character is in, open or completing, or none.</summary>
    public Trade? TradeOf(uint characterId) => _tradeOf.GetValueOrDefault(characterId);

    /// <summary>Whether this character's one request is taken: one it sent, or a visible one it received.</summary>
    public bool HoldsRequest(uint characterId) => _requestOf.ContainsKey(characterId);

    public TradeResult Request(uint senderId, uint targetId)
    {
        tickThread?.AssertOnTick("TradeService.Request");
        if (online.ById(senderId)?.Character is not CharacterEntity sender)
            return TradeResult.NotFound;
        if (sender.IsDead)
            return TradeResult.Dead;
        if (sender.IsInCombat)
            return TradeResult.InCombat;
        if (HasNpcWindowOpen(sender))
            return TradeResult.NpcWindowOpen;
        if (_tradeOf.ContainsKey(senderId) || _requestOf.ContainsKey(senderId))
            return TradeResult.Busy;

        // The request budget, checked before the target is looked up, so a request past it costs no lookup. Every
        // request that gets this far spends one, whatever the lookup finds.
        if (!_requestBudget.Check(senderId, out _))
            return TradeResult.TooFast;
        _requestBudget.Record(senderId);

        if (targetId == senderId)
            return TradeResult.Self;
        if (online.ById(targetId) is not { Character: CharacterEntity target } targetConnection
            || target.InstanceId != sender.InstanceId)
        {
            return TradeResult.NotFound;
        }

        if (target.IsDead)
            return TradeResult.TargetDead;
        if (target.IsInCombat)
            return TradeResult.TargetInCombat;
        if (!TradeRules.WithinRange(sender.Position, target.Position, TradeRules.RequestRange))
            return TradeResult.TooFar;
        if (_tradeOf.ContainsKey(targetId) || _requestOf.ContainsKey(targetId))
            return TradeResult.TargetBusy;

        var timeout = TimeSpan.FromSeconds(Config.TradeRequestTimeoutSeconds);
        // A target ignoring the sender never sees the request. Only after every refusal, so the sender is answered
        // exactly as for a sent request, and later told it expired as a sent one would be.
        bool hidden = Ignoring.Hides(targetConnection, senderId);
        var request = new TradeRequest(senderId, targetId, sender.Name, target.Name, time.GetUtcNow() + timeout, hidden);
        _requests.Add(request);
        _requestOf[senderId] = request;
        if (hidden)
            return TradeResult.Ok;

        _requestOf[targetId] = request;
        targetConnection.Send(STradeRequestPacket.Create(senderId, sender.Name, sender.Class, sender.Level,
            (uint)timeout.TotalMilliseconds, PacketEncoder.Shared));
        return TradeResult.Ok;
    }

    /// <summary>
    /// The target's answer to the request it holds. A decline ends it and tells the sender. An accept that fails a
    /// condition answers why, from the accepter's side, and leaves the request open until it expires.
    /// </summary>
    public TradeResult Respond(uint targetId, bool accept)
    {
        tickThread?.AssertOnTick("TradeService.Respond");
        if (!_requestOf.TryGetValue(targetId, out TradeRequest? request) || request.TargetId != targetId)
            return TradeResult.NoRequest;

        if (!accept)
        {
            EndRequest(request, _requests.IndexOf(request));
            Tell(request.SenderId, TradeResult.Declined, request.TargetName);
            return TradeResult.Ok;
        }

        if (online.ById(targetId) is not { Character: CharacterEntity target } targetConnection)
            return TradeResult.NotFound;
        if (target.IsDead)
            return TradeResult.Dead;
        if (target.IsInCombat)
            return TradeResult.InCombat;
        if (HasNpcWindowOpen(target))
            return TradeResult.NpcWindowOpen;

        if (online.ById(request.SenderId) is not { Character: CharacterEntity sender } senderConnection
            || sender.InstanceId != target.InstanceId)
        {
            return TradeResult.NotFound;
        }

        if (sender.IsDead)
            return TradeResult.TargetDead;
        if (sender.IsInCombat)
            return TradeResult.TargetInCombat;
        if (HasNpcWindowOpen(sender))
            return TradeResult.TargetNpcWindowOpen;
        if (!TradeRules.WithinRange(sender.Position, target.Position, TradeRules.KeepRange))
            return TradeResult.TooFar;

        EndRequest(request, _requests.IndexOf(request));
        var trade = new Trade(++_lastTradeId,
            new TradeSide(senderConnection, sender), new TradeSide(targetConnection, target));
        _trades.Add(trade);
        _tradeOf[request.SenderId] = trade;
        _tradeOf[targetId] = trade;

        senderConnection.Send(STradeOpenedPacket.Create(trade.Id, targetId, target.Name, PacketEncoder.Shared));
        targetConnection.Send(STradeOpenedPacket.Create(trade.Id, request.SenderId, sender.Name, PacketEncoder.Shared));
        SendState(trade);
        LogOpened(logger, trade.Id, request.SenderId, targetId);
        return TradeResult.Ok;
    }

    /// <summary>The whole stack at <paramref name="bagSlot" /> into a free trade slot: it stays in the Bag, locked.</summary>
    public TradeResult OfferItem(uint characterId, byte bagSlot, byte tradeSlot)
    {
        tickThread?.AssertOnTick("TradeService.OfferItem");
        if (Editable(characterId, out Trade? trade) is { } refused)
            return refused;

        TradeSide side = trade.SideOf(characterId);
        CharacterInventoryContainer bag = side.Character.Container(InventoryType.Bag);
        if (tradeSlot >= TradeRules.MaxItems || bagSlot >= bag.Capacity)
            return TradeResult.InvalidSlot;
        if (!bag.TryGet(bagSlot, out InventoryItem item))
            return TradeResult.ItemNotFound;
        if (Offers(side, item.InstanceId))
            return TradeResult.AlreadyOffered;
        if (side.Items[tradeSlot] is not null)
            return TradeResult.TradeSlotTaken;
        if (!TradeRules.IsTradeable(_findTemplate(item.TemplateId)))
            return TradeResult.NotTradeable;

        side.Items[tradeSlot] = item;
        side.Character.Locks.Lock(item.InstanceId);
        Changed(trade);
        return TradeResult.Ok;
    }

    /// <summary>Takes the item in <paramref name="tradeSlot" /> back out of the trade, which unlocks it.</summary>
    public TradeResult RemoveItem(uint characterId, byte tradeSlot)
    {
        tickThread?.AssertOnTick("TradeService.RemoveItem");
        if (Editable(characterId, out Trade? trade) is { } refused)
            return refused;

        TradeSide side = trade.SideOf(characterId);
        if (tradeSlot >= TradeRules.MaxItems)
            return TradeResult.InvalidSlot;
        if (side.Items[tradeSlot] is not { } item)
            return TradeResult.ItemNotFound;

        side.Items[tradeSlot] = null;
        side.Character.Locks.Unlock(item.InstanceId);
        Changed(trade);
        return TradeResult.Ok;
    }

    /// <summary>The gold this side offers, from 0 up to its balance. It is held, not spendable, while offered.</summary>
    public TradeResult SetGold(uint characterId, ulong gold)
    {
        tickThread?.AssertOnTick("TradeService.SetGold");
        if (Editable(characterId, out Trade? trade) is { } refused)
            return refused;

        TradeSide side = trade.SideOf(characterId);
        if (gold > (side.Character.Data?.Gold ?? 0))
            return TradeResult.NotEnoughGold;
        if (gold == side.Gold)
            return TradeResult.Ok;

        side.Gold = gold;
        side.Character.Locks.HeldGold = gold;
        Changed(trade);
        return TradeResult.Ok;
    }

    /// <summary>
    /// Confirms the offer as it stands at <paramref name="revision" />. A confirm for an older revision is refused, and the
    /// confirmer is sent the trade as it now stands.
    /// </summary>
    public TradeResult Confirm(uint characterId, uint revision)
    {
        tickThread?.AssertOnTick("TradeService.Confirm");
        if (Editable(characterId, out Trade? trade) is { } refused)
            return refused;

        TradeSide side = trade.SideOf(characterId);
        if (revision != trade.Revision)
        {
            SendState(trade, side);
            return TradeResult.StaleOffer;
        }

        if (side.Confirmed)
            return TradeResult.Ok;

        side.Confirmed = true;
        SendState(trade);
        if (trade.First.Confirmed && trade.Second.Confirmed)
            OnBothConfirmed(trade);
        return TradeResult.Ok;
    }

    /// <summary>Both sides confirmed the current revision: the trade completes (TradeService.Completion.cs).</summary>
    partial void OnBothConfirmed(Trade trade);

    /// <summary>Closes the trade for both; nothing moves.</summary>
    public TradeResult Cancel(uint characterId)
    {
        tickThread?.AssertOnTick("TradeService.Cancel");
        if (Editable(characterId, out Trade? trade) is { } refused)
            return refused;

        bool first = trade.First.Character.Guid.Id == characterId;
        Close(trade,
            first ? TradeResult.Cancelled : TradeResult.CancelledByPartner,
            first ? TradeResult.CancelledByPartner : TradeResult.Cancelled);
        return TradeResult.Ok;
    }

    /// <summary>
    /// Once per tick, from World.Update. Returns at once with no request and no trade. While a trade completes, applies
    /// what its save and record lookups learned and starts the lookups that are due (TradeService.Completion.cs). Then
    /// expires requests, and closes each open trade with the first automatic cancel that holds, the same reason to both.
    /// </summary>
    public void Tick()
    {
        if (_requests.Count == 0 && _trades.Count == 0)
            return;

        tickThread?.AssertOnTick("TradeService.Tick");
        if (_completions.Count > 0)
            AdvanceCompletions(time.GetUtcNow());

        if (_requests.Count > 0)
            ExpireRequests(time.GetUtcNow());

        // Backwards, so a trade closed here leaves the ones still to visit where they were.
        for (int i = _trades.Count - 1; i >= 0; i--)
        {
            Trade trade = _trades[i];
            if (trade.State != TradeState.Open)
                continue;

            TradeResult reason = CancelReason(trade);
            if (reason != TradeResult.Ok)
                Close(trade, reason, reason, i);
        }
    }

    /// <summary>
    /// The character left the world (World.LeaveWorldAsync, before the connection lets go of it): every request it sent
    /// or held ends, the other side told it expired; its budget is forgotten; an open trade closes with
    /// <see cref="TradeResult.Left" />, which only the partner hears. A completing trade is not cancelled: the side is
    /// marked offline, and the outcome is applied to its entity, which the held despawn save snapshots at the release.
    /// A stale connection's leave changes nothing.
    /// </summary>
    public void CharacterOffline(IWorldConnection connection, ICharacter character)
    {
        tickThread?.AssertOnTick("TradeService.CharacterOffline");
        uint id = character.Guid.Id;
        if (online.ById(id) is { } current && !ReferenceEquals(current, connection))
            return;

        EndRequestsOf(id, tellSelf: false);
        _requestBudget.Forget(id);

        if (!_tradeOf.TryGetValue(id, out Trade? trade) || !ReferenceEquals(trade.SideOf(id).Connection, connection))
            return;

        if (trade.State == TradeState.Completing)
        {
            trade.SideOf(id).Online = false;
            return;
        }

        bool first = trade.First.Character.Guid.Id == id;
        Close(trade, first ? TradeResult.Unknown : TradeResult.Left, first ? TradeResult.Left : TradeResult.Unknown);
    }

    /// <summary>
    /// The character moved to another instance (World.TransferPlayer): its requests end, both sides told they expired, and
    /// an open trade closes with <see cref="TradeResult.Left" /> to both. A completing trade is left to its outcome.
    /// </summary>
    public void InstanceChanged(IWorldConnection connection)
    {
        tickThread?.AssertOnTick("TradeService.InstanceChanged");
        if (connection.Character is not { } character)
            return;

        uint id = character.Guid.Id;
        EndRequestsOf(id, tellSelf: true);
        if (_tradeOf.TryGetValue(id, out Trade? trade) && trade.State == TradeState.Open)
            Close(trade, TradeResult.Left, TradeResult.Left);
    }

    /// <summary>
    /// The character is opening a shop or the bank (the NPC window paths): an open trade closes. A completing trade is
    /// left to its outcome.
    /// </summary>
    public void NpcWindowOpening(ICharacter character)
    {
        tickThread?.AssertOnTick("TradeService.NpcWindowOpening");
        if (_tradeOf.TryGetValue(character.Guid.Id, out Trade? trade) && trade.State == TradeState.Open)
            Close(trade, TradeResult.NpcWindowOpened, TradeResult.NpcWindowOpened);
    }

    private static bool HasNpcWindowOpen(CharacterEntity character) =>
        character.OpenShopNpc is not null || character.OpenBankNpc is not null;

    private static bool Offers(TradeSide side, ItemInstanceId instance)
    {
        for (int i = 0; i < side.Items.Length; i++)
        {
            if (side.Items[i] is { } offered && offered.InstanceId.Value == instance.Value)
                return true;
        }

        return false;
    }

    /// <summary>The refusal for an edit, a confirm or a cancel, or null with the open trade.</summary>
    private TradeResult? Editable(uint characterId, out Trade trade)
    {
        if (!_tradeOf.TryGetValue(characterId, out Trade? found))
        {
            trade = null!;
            return TradeResult.NotTrading;
        }

        trade = found;
        return found.State == TradeState.Completing ? TradeResult.Completing : null;
    }

    /// <summary>An offer changed: both confirmations clear, the revision moves on and both are sent the trade.</summary>
    private void Changed(Trade trade)
    {
        trade.First.Confirmed = false;
        trade.Second.Confirmed = false;
        trade.Revision++;
        SendState(trade);
    }

    /// <summary>The first automatic cancel that holds for an open trade, or <see cref="TradeResult.Ok" />.</summary>
    private static TradeResult CancelReason(Trade trade)
    {
        CharacterEntity first = trade.First.Character;
        CharacterEntity second = trade.Second.Character;
        if (!ReferenceEquals(trade.First.Connection.Character, first)
            || !ReferenceEquals(trade.Second.Connection.Character, second)
            || first.InstanceId != second.InstanceId)
        {
            return TradeResult.Left;
        }

        if (first.IsDead || second.IsDead)
            return TradeResult.Died;
        if (first.IsInCombat || second.IsInCombat)
            return TradeResult.EnteredCombat;
        if (HasNpcWindowOpen(first) || HasNpcWindowOpen(second))
            return TradeResult.NpcWindowOpened;
        if (!TradeRules.WithinRange(first.Position, second.Position, TradeRules.KeepRange))
            return TradeResult.OutOfRange;
        return TradeResult.Ok;
    }

    /// <summary>
    /// Ends the trade: both sides' locks released, the trade forgotten, and each side told its result
    /// (<see cref="TradeResult.Unknown" /> tells that side nothing). A side whose connection no longer holds its character
    /// is told nothing either.
    /// </summary>
    /// <param name="index">The trade's index in the list when the caller has it (the sweep); otherwise looked up.</param>
    private void Close(Trade trade, TradeResult toFirst, TradeResult toSecond, int index = -1)
    {
        int at = index >= 0 ? index : _trades.IndexOf(trade);
        if (at >= 0)
            _trades.RemoveAt(at);
        _tradeOf.Remove(trade.First.Character.Guid.Id);
        _tradeOf.Remove(trade.Second.Character.Guid.Id);
        trade.First.Character.Locks.Clear();
        trade.Second.Character.Locks.Clear();

        TellClosed(trade, trade.First, toFirst);
        TellClosed(trade, trade.Second, toSecond);
        LogClosed(logger, trade.Id, toFirst, trade.First.Character.Guid.Id, toSecond, trade.Second.Character.Guid.Id);
    }

    private static void TellClosed(Trade trade, TradeSide side, TradeResult result)
    {
        if (result != TradeResult.Unknown && ReferenceEquals(side.Connection.Character, side.Character))
            side.Connection.Send(STradeClosedPacket.Create(trade.Id, result, PacketEncoder.Shared));
    }

    private void ExpireRequests(DateTimeOffset now)
    {
        for (int i = _requests.Count - 1; i >= 0; i--)
        {
            TradeRequest request = _requests[i];
            if (request.ExpiresAt > now)
                continue;

            EndRequest(request, i);
            Tell(request.SenderId, TradeResult.Expired, request.TargetName);
            if (!request.Hidden)
                Tell(request.TargetId, TradeResult.Expired, request.SenderName);
        }
    }

    /// <summary>
    /// Every request this character sent or received ends. The other side is told it expired, but the target of a hidden
    /// request, which never saw it; with <paramref name="tellSelf" />, so is this character, for each it saw.
    /// </summary>
    private void EndRequestsOf(uint characterId, bool tellSelf)
    {
        for (int i = _requests.Count - 1; i >= 0; i--)
        {
            TradeRequest request = _requests[i];
            bool sent = request.SenderId == characterId;
            if (!sent && request.TargetId != characterId)
                continue;

            EndRequest(request, i);
            if (sent)
            {
                if (!request.Hidden)
                    Tell(request.TargetId, TradeResult.Expired, request.SenderName);
                if (tellSelf)
                    Tell(characterId, TradeResult.Expired, request.TargetName);
            }
            else
            {
                Tell(request.SenderId, TradeResult.Expired, request.TargetName);
                if (tellSelf && !request.Hidden)
                    Tell(characterId, TradeResult.Expired, request.SenderName);
            }
        }
    }

    /// <param name="index">The request's index in <see cref="_requests" />.</param>
    private void EndRequest(TradeRequest request, int index)
    {
        _requests.RemoveAt(index);
        _requestOf.Remove(request.SenderId);
        if (!request.Hidden)
            _requestOf.Remove(request.TargetId);
    }

    private void Tell(uint characterId, TradeResult result, string? name)
    {
        if (online.ById(characterId) is { } connection)
            connection.Send(STradeResultPacket.Create(result, name, PacketEncoder.Shared));
    }

    /// <summary>The trade to both sides: each side is filled once, and each recipient sees its own as You.</summary>
    private void SendState(Trade trade)
    {
        FillSides(trade);
        Send(trade, trade.First, _sideDtos[0], _sideDtos[1]);
        Send(trade, trade.Second, _sideDtos[1], _sideDtos[0]);
    }

    /// <summary>The trade to one side only.</summary>
    private void SendState(Trade trade, TradeSide recipient)
    {
        FillSides(trade);
        bool first = ReferenceEquals(recipient, trade.First);
        Send(trade, recipient, _sideDtos[first ? 0 : 1], _sideDtos[first ? 1 : 0]);
    }

    private static void Send(Trade trade, TradeSide recipient, TradeSideDto you, TradeSideDto partner) =>
        recipient.Connection.Send(STradeStatePacket.Create(trade.Id, trade.Revision, trade.State == TradeState.Completing,
            you, partner, PacketEncoder.Shared));

    private void FillSides(Trade trade)
    {
        Fill(0, trade.First);
        Fill(1, trade.Second);
    }

    private void Fill(int index, TradeSide side)
    {
        TradeSideDto dto = _sideDtos[index];
        List<TradeItemDto> items = _itemLists[index];
        items.Clear();
        for (int slot = 0; slot < side.Items.Length; slot++)
        {
            if (side.Items[slot] is not { } item)
                continue;

            TradeItemDto entry = _itemDtos[(index * TradeRules.MaxItems) + slot];
            entry.TradeSlot = (byte)slot;
            entry.ItemTemplateId = item.TemplateId.Value;
            entry.ItemInstanceId = item.InstanceId.Value;
            entry.Count = item.Count;
            // An offered item stays in its owner's Bag, locked, until the swap, so the store still holds its modifiers.
            ItemDetailsDtoMapper.Refill(entry.Details, item, side.Character.ItemModifiers.Of(item.InstanceId));
            items.Add(entry);
        }

        dto.Gold = side.Gold;
        dto.Confirmed = side.Confirmed;
        dto.Items = items;
    }

    private static TradeItemDto[] NewItemDtos()
    {
        var dtos = new TradeItemDto[2 * TradeRules.MaxItems];
        for (int i = 0; i < dtos.Length; i++)
            dtos[i] = new TradeItemDto { Details = new ItemDetailsDto() };
        return dtos;
    }

    // Debug lines through generated methods, which check the level before formatting: no boxing with Debug off.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Trade {TradeId} opened between characters {First} and {Second}")]
    private static partial void LogOpened(ILogger logger, uint tradeId, uint first, uint second);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Trade {TradeId} closed: {First} for character {FirstId}, {Second} for character {SecondId}")]
    private static partial void LogClosed(ILogger logger, uint tradeId, TradeResult first, uint firstId,
        TradeResult second, uint secondId);

    /// <summary>A request, found and removed by reference.</summary>
    /// <param name="hidden">Its target ignores its sender: never sent, and indexed under its sender only.</param>
    private sealed class TradeRequest(uint senderId, uint targetId, string senderName, string targetName,
        DateTimeOffset expiresAt, bool hidden)
    {
        public uint SenderId { get; } = senderId;

        public uint TargetId { get; } = targetId;

        public string SenderName { get; } = senderName;

        public string TargetName { get; } = targetName;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public bool Hidden { get; } = hidden;
    }
}
