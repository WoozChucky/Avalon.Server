using System.Collections.Concurrent;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Trade;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Trading;

/// <summary>
/// Completion (#1175): save first, then swap. Both confirmations start the paired save, which writes both characters
/// with the trade applied and the trade's record in one transaction, and holds every other save of the two until
/// <see cref="ICharacterSaver.ReleaseHold" />. Memory changes only once the save is known to have committed: on the
/// outcome Committed, or on Unknown when the record is found. Refused, or no record, cancels with nothing moved. Every
/// path after a SaveTrade that did not throw releases both holds, but one: a trade still unsettled at the shutdown limit
/// keeps them, so nothing pre-trade is written over a trade that may have committed.
/// The save and the lookups run off the tick; their continuations only enqueue what they learned, and Tick (or, at
/// shutdown, <see cref="SettleAsync" />) applies it.
/// </summary>
public sealed partial class TradeService
{
    /// <summary>The longest wait between two record lookups of one trade.</summary>
    private const int MaxLookupBackoffSeconds = 30;

    /// <summary>The longest single wait of <see cref="SettleAsync" /> before it looks again.</summary>
    private static readonly TimeSpan s_settlePoll = TimeSpan.FromSeconds(30);

    // Every completing trade, by index, and what their saves and lookups learned, filled off the tick.
    private readonly List<Completion> _completions = [];
    private readonly ConcurrentQueue<CompletionEvent> _completionEvents = new();

    /// <summary>
    /// For shutdown, once the tick has stopped: waits for every completing trade's save, and looks up the record of each
    /// unknown outcome with the same backoff, all within <paramref name="limit" /> and <paramref name="token" />, and
    /// applies each outcome as Tick does. A trade still unsettled then is logged at Error, and keeps its holds: those
    /// two characters' later saves are not written, nothing is duplicated, and the record tells an operator whether the
    /// trade happened.
    /// </summary>
    /// <returns>Whether every completing trade settled: false while one keeps its holds.</returns>
    public async Task<bool> SettleAsync(TimeSpan limit, CancellationToken token)
    {
        // Free at shutdown, where the tick has unbound the guard; it refuses a call while a tick still runs.
        tickThread?.AssertOnTick("TradeService.SettleAsync");
        if (_completions.Count == 0)
            return true;

        DateTimeOffset start = time.GetUtcNow();
        DateTimeOffset deadline = limit >= DateTimeOffset.MaxValue - start ? DateTimeOffset.MaxValue : start + limit;
        while (true)
        {
            DateTimeOffset now = time.GetUtcNow();
            AdvanceCompletions(now);
            if (_completions.Count == 0)
                return true;
            if (now >= deadline || token.IsCancellationRequested)
                break;

            // Wake on whatever comes first: a save or lookup finishing, the next retry falling due, or the limit.
            DateTimeOffset wakeAt = deadline;
            var waits = new List<Task>(_completions.Count + 1);
            foreach (Completion completion in _completions)
            {
                if (completion.InFlight)
                    waits.Add(completion.Pending);
                else if (completion.LookupDueAt is { } due && due < wakeAt)
                    wakeAt = due;
            }

            TimeSpan wait = wakeAt - now;
            if (wait <= TimeSpan.Zero)
                continue;

            using var wake = CancellationTokenSource.CreateLinkedTokenSource(token);
            waits.Add(Task.Delay(wait < s_settlePoll ? wait : s_settlePoll, time, wake.Token));
            await Task.WhenAny(waits).ConfigureAwait(false);
            await wake.CancelAsync().ConfigureAwait(false);
        }

        foreach (Completion completion in _completions)
        {
            logger.LogError(
                "Trade {TradeId} between characters {First} and {Second} is unsettled at shutdown: its record {RecordId} " +
                "says whether it committed. Both characters stay held, so neither's later saves are written",
                completion.Trade.Id, completion.Trade.First.Character.Guid.Id, completion.Trade.Second.Character.Guid.Id,
                completion.Trade.RecordId);
        }

        return false;
    }

    /// <summary>
    /// Both sides confirmed the current revision. The player checks first (a failure closes the trade, as the automatic
    /// cancel would), then the completion rules (a failure keeps the window open with both confirmations cleared), then
    /// the paired save. Nothing in memory moves here.
    /// </summary>
    partial void OnBothConfirmed(Trade trade)
    {
        TradeResult reason = CancelReason(trade);
        if (reason != TradeResult.Ok)
        {
            Close(trade, reason, reason);
            return;
        }

        TradeSide first = trade.First;
        TradeSide second = trade.Second;
        TradeDecision decision = TradeRules.Plan(first.Offer(), second.Offer(), _findTemplate, Config.MaxGold);
        if (decision.Plan is not { } plan)
        {
            (TradeSide failing, TradeSide partner) = decision.FailingSide == 0 ? (first, second) : (second, first);
            TellSide(failing, decision.Result, name: null);
            TellSide(partner, PartnerResult(decision.Result), failing.Character.Name);
            Changed(trade);
            return;
        }

        Task<SaveOutcome> save;
        try
        {
            trade.State = TradeState.Completing;
            ExpectIncoming(first, plan.First);
            ExpectIncoming(second, plan.Second);

            TradeRecord record = NewRecord(first, second, plan);
            trade.RecordId = record.Id;
            save = _saver.SaveTrade(
                [(first.Connection, first.Character, plan.First), (second.Connection, second.Character, plan.Second)],
                record);
        }
        catch (Exception ex)
        {
            // Nothing is written and nothing is held: SaveTrade throws only before it queues anything.
            logger.LogError(ex, "Trade {TradeId} between characters {First} and {Second} could not start its save",
                trade.Id, first.Character.Guid.Id, second.Character.Guid.Id);
            first.Character.Locks.Clear();
            second.Character.Locks.Clear();
            Close(trade, TradeResult.Error, TradeResult.Error);
            return;
        }

        var completion = new Completion(trade, plan);
        _completions.Add(completion);
        completion.InFlight = true;
        completion.Pending = ObserveSave(completion, save);
        SendState(trade);
    }

    /// <summary>
    /// Tick thread, or SettleAsync once the tick has stopped: applies what the saves and lookups learned, then starts
    /// every record lookup that is due.
    /// </summary>
    private void AdvanceCompletions(DateTimeOffset now)
    {
        while (_completionEvents.TryDequeue(out CompletionEvent next))
        {
            // One failure (a hold release that throws, after the trade has closed) never keeps the other completions
            // from being applied: at shutdown that would leave every one of them unsettled, holds kept.
            try
            {
                Handle(next, now);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trade {TradeId}: applying its outcome failed", next.Completion.Trade.Id);
            }
        }

        for (int i = 0; i < _completions.Count; i++)
        {
            Completion completion = _completions[i];
            if (!completion.InFlight && completion.LookupDueAt is { } due && now >= due)
                StartLookup(completion);
        }
    }

    private void Handle(CompletionEvent next, DateTimeOffset now)
    {
        Completion completion = next.Completion;
        if (completion.Settled)
            return;

        switch (next.Step)
        {
            case CompletionStep.Committed:
                Commit(completion);
                break;

            case CompletionStep.Refused:
                Refuse(completion);
                break;

            case CompletionStep.Unknown:
                // Never guessed: the trade stays completing, with its locks, reservations and holds, until its record
                // is read. The first lookup starts on this tick.
                completion.InFlight = false;
                completion.LookupDueAt = now;
                logger.LogWarning(next.Error,
                    "Trade {TradeId}: whether its save committed is unknown; looking up its record {RecordId}",
                    completion.Trade.Id, completion.Trade.RecordId);
                break;

            case CompletionStep.LookupFailed:
                completion.InFlight = false;
                completion.LookupDueAt = now + TimeSpan.FromSeconds(LookupBackoffSeconds(completion.LookupAttempts));
                logger.LogWarning(next.Error,
                    "Trade {TradeId}: looking up its record {RecordId} failed (attempt {Attempt}); trying again",
                    completion.Trade.Id, completion.Trade.RecordId, completion.LookupAttempts);
                break;
        }
    }

    private void StartLookup(Completion completion)
    {
        completion.LookupAttempts++;
        completion.LookupDueAt = null;
        completion.InFlight = true;

        Task<bool> lookup;
        try
        {
            lookup = _saver.TradeCommittedAsync(completion.Trade.RecordId,
                completion.Trade.First.Character.Data!.AccountId, completion.Trade.Second.Character.Data!.AccountId);
        }
        catch (Exception ex)
        {
            lookup = Task.FromException<bool>(ex);
        }

        completion.Pending = ObserveLookup(completion, lookup);
    }

    /// <summary>
    /// Off the tick, on whichever thread completes the save: only enqueues its outcome. A save task never faults by
    /// contract; one that did is settled by the record, never guessed. Runs synchronously with that completion, so the
    /// outcome is queued before anything waiting on the save resumes.
    /// </summary>
    private Task ObserveSave(Completion completion, Task<SaveOutcome> save) =>
        save.ContinueWith(
            t => _completionEvents.Enqueue(new CompletionEvent(completion, StepOf(t), t.Exception)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>Off the tick, as <see cref="ObserveSave" />: only enqueues what the lookup found, or that it failed.</summary>
    private Task ObserveLookup(Completion completion, Task<bool> lookup) =>
        lookup.ContinueWith(
            t => _completionEvents.Enqueue(new CompletionEvent(completion, StepOf(t), t.Exception)),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

#pragma warning disable MA0045 // each task has completed successfully: reading its result does not block
    private static CompletionStep StepOf(Task<SaveOutcome> save) =>
        !save.IsCompletedSuccessfully ? CompletionStep.Unknown : save.Result switch
        {
            SaveOutcome.Committed => CompletionStep.Committed,
            SaveOutcome.Refused => CompletionStep.Refused,
            _ => CompletionStep.Unknown,
        };

    private static CompletionStep StepOf(Task<bool> lookup) =>
        !lookup.IsCompletedSuccessfully ? CompletionStep.LookupFailed
        : lookup.Result ? CompletionStep.Committed : CompletionStep.Refused;
#pragma warning restore MA0045

    /// <summary>
    /// The save committed: the swap, through the inventory services and wallets only, then both holds released (each
    /// held save is snapshotted after the swap) and the trade closed with Ok for each side still online.
    /// </summary>
    private void Commit(Completion completion)
    {
        completion.Settled = true;
        _completions.Remove(completion);
        Trade trade = completion.Trade;
        try
        {
            Swap(trade, completion.Plan);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "Trade {TradeId} committed, but applying it to characters {First} and {Second} failed: memory differs " +
                "from the database until their next login", trade.Id, trade.First.Character.Guid.Id,
                trade.Second.Character.Guid.Id);
        }
        finally
        {
            ReleaseAndClose(trade, TradeResult.Ok);
        }
    }

    /// <summary>The save was refused, or its record was not found: nothing moved, and nothing will.</summary>
    private void Refuse(Completion completion)
    {
        completion.Settled = true;
        _completions.Remove(completion);
        Trade trade = completion.Trade;
        trade.First.Character.Locks.Clear();
        trade.Second.Character.Locks.Clear();
        logger.LogWarning("Trade {TradeId} between characters {First} and {Second} was not saved; nothing moved",
            trade.Id, trade.First.Character.Guid.Id, trade.Second.Character.Guid.Id);
        ReleaseAndClose(trade, TradeResult.SaveFailed);
    }

    /// <summary>
    /// Both holds released, each even if the other's release throws, then the trade closed with
    /// <paramref name="result" /> for each side still online.
    /// </summary>
    private void ReleaseAndClose(Trade trade, TradeResult result)
    {
        try
        {
            _saver.ReleaseHold(trade.First.Character.Data!.Id);
        }
        finally
        {
            try
            {
                _saver.ReleaseHold(trade.Second.Character.Data!.Id);
            }
            finally
            {
                Close(trade,
                    trade.First.Online ? result : TradeResult.Unknown,
                    trade.Second.Online ? result : TradeResult.Unknown);
            }
        }
    }

    /// <summary>
    /// Applies the committed plan: every item out of each giver, then every item into each receiver at its planned
    /// slot, then each side's gold spent, then received. A refusal is logged at Critical; what a giver did not lose its
    /// receiver does not gain, so memory never holds more than the database. The audit line says whether every step
    /// applied.
    /// </summary>
    private void Swap(Trade trade, TradePlan plan)
    {
        ICharacterEconomy economy = _economy
            ?? throw new InvalidOperationException("TradeService.Attach has not been called.");
        TradeSide first = trade.First;
        TradeSide second = trade.Second;
        first.Character.Locks.Clear();
        second.Character.Locks.Clear();

        bool clean = true;
        bool[] firstGave = TakeOut(trade, first, plan.First.Outgoing, economy, ref clean);
        bool[] secondGave = TakeOut(trade, second, plan.Second.Outgoing, economy, ref clean);

        // Each side's incoming items are the partner's outgoing ones, in the same order.
        clean &= PutIn(trade, first, plan.First.Incoming, secondGave, economy);
        clean &= PutIn(trade, second, plan.Second.Incoming, firstGave, economy);

        IWallet firstWallet = economy.WalletOf(first.Character);
        IWallet secondWallet = economy.WalletOf(second.Character);
        bool firstPaid = Spend(trade, first, firstWallet, plan.First.GoldOut);
        bool secondPaid = Spend(trade, second, secondWallet, plan.Second.GoldOut);
        clean &= firstPaid & secondPaid;
        clean &= Receive(trade, first, firstWallet, secondPaid ? plan.First.GoldIn : 0);
        clean &= Receive(trade, second, secondWallet, firstPaid ? plan.Second.GoldIn : 0);

        if (clean)
        {
            logger.LogInformation(
                "Trade {TradeId} completed: character {First} gave [{FirstItems}] and {FirstGold} gold, character " +
                "{Second} gave [{SecondItems}] and {SecondGold} gold (record {RecordId})",
                trade.Id, first.Character.Guid.Id, Describe(plan.First.Outgoing), plan.First.GoldOut,
                second.Character.Guid.Id, Describe(plan.Second.Outgoing), plan.Second.GoldOut, trade.RecordId);
        }
        else
        {
            logger.LogError(
                "Trade {TradeId} completed with errors (see the Critical lines above): the database has character " +
                "{First} giving [{FirstItems}] and {FirstGold} gold, character {Second} giving [{SecondItems}] and " +
                "{SecondGold} gold (record {RecordId}), and memory differs from it until their next login",
                trade.Id, first.Character.Guid.Id, Describe(plan.First.Outgoing), plan.First.GoldOut,
                second.Character.Guid.Id, Describe(plan.Second.Outgoing), plan.Second.GoldOut, trade.RecordId);
        }
    }

    private bool[] TakeOut(
        Trade trade, TradeSide giver, IReadOnlyList<InventoryItem> outgoing, ICharacterEconomy economy, ref bool clean)
    {
        IInventoryService inventory = economy.InventoryOf(giver.Character);
        CharacterInventoryContainer bag = giver.Character.Container(InventoryType.Bag);
        bool[] gave = new bool[outgoing.Count];
        for (int i = 0; i < outgoing.Count; i++)
        {
            InventoryItem item = outgoing[i];
            // The whole stack, exactly as saved: anything else would hand over an instance the database does not name.
            if (!bag.TryGet(item.Slot, out InventoryItem held) || held.InstanceId != item.InstanceId
                || held.Count != item.Count)
            {
                // The item stays with the giver in memory while the database gives it to the receiver, and its handover
                // is not marked: the giver's next change to it marks an item the database no longer gives the giver,
                // and the save fence refuses every save of the giver from then until logout.
                logger.LogCritical(
                    "Trade {TradeId}: item {ItemId} (template {TemplateId}) is no longer at Bag slot {Slot} of character " +
                    "{Character}; the database has the trade, and memory differs from it until the next login. A later " +
                    "change to that item makes the save fence refuse the character's saves until logout",
                    trade.Id, item.InstanceId.Value, item.TemplateId.Value, item.Slot, giver.Character.Guid.Id);
                clean = false;
                continue;
            }

            inventory.TakeOut(new SlotRef(InventoryType.Bag, item.Slot), item.Count);
            giver.Character.SaveState.ItemHandedOver(item.InstanceId);
            gave[i] = true;
        }

        return gave;
    }

    private bool PutIn(Trade trade, TradeSide receiver, IReadOnlyList<InventoryItem> incoming, bool[] given,
        ICharacterEconomy economy)
    {
        IInventoryService inventory = economy.InventoryOf(receiver.Character);
        bool clean = true;
        for (int i = 0; i < incoming.Count; i++)
        {
            InventoryItem item = incoming[i];
            if (!given[i])
                continue;

            InventoryAddResult added = inventory.TryAddInstanceAt(item, item.Slot);
            if (added != InventoryAddResult.Ok)
            {
                logger.LogCritical(
                    "Trade {TradeId}: item {ItemId} (template {TemplateId}) could not be added to Bag slot {Slot} of " +
                    "character {Character} ({Result}); the database has the trade, and memory differs from it until " +
                    "the next login", trade.Id, item.InstanceId.Value, item.TemplateId.Value, item.Slot,
                    receiver.Character.Guid.Id, added);
                clean = false;
            }
        }

        return clean;
    }

    private bool Spend(Trade trade, TradeSide side, IWallet wallet, ulong gold)
    {
        WalletResult spent = wallet.TrySpend(gold);
        if (spent == WalletResult.Ok)
            return true;

        logger.LogCritical(
            "Trade {TradeId}: character {Character} could not pay {Gold} gold ({Result}); the database has the trade, " +
            "and memory differs from it until the next login", trade.Id, side.Character.Guid.Id, gold, spent);
        return false;
    }

    private bool Receive(Trade trade, TradeSide side, IWallet wallet, ulong gold)
    {
        WalletResult added = wallet.TryAddGold(gold);
        if (added == WalletResult.Ok)
            return true;

        logger.LogCritical(
            "Trade {TradeId}: character {Character} could not receive {Gold} gold ({Result}); the database has the " +
            "trade, and memory differs from it until the next login", trade.Id, side.Character.Guid.Id, gold, added);
        return false;
    }

    /// <summary>
    /// While the save is in flight, the receiver's destination slots are reserved, and the incoming gold and copies
    /// expected, so no other add takes the room, the gold headroom or the one Unique copy the trade planned on.
    /// </summary>
    private static void ExpectIncoming(TradeSide side, TradeSidePlan plan)
    {
        InventoryLocks locks = side.Character.Locks;
        foreach (InventoryItem item in plan.Incoming)
        {
            locks.Reserve(item.Slot);
            locks.ExpectIncoming(item.TemplateId, item.Count);
        }

        locks.IncomingGold = plan.GoldIn;
    }

    private TradeRecord NewRecord(TradeSide first, TradeSide second, TradePlan plan)
    {
        var record = new TradeRecord
        {
            Id = Guid.CreateVersion7(),
            FirstCharacterId = first.Character.Data!.Id,
            SecondCharacterId = second.Character.Data!.Id,
            FirstGold = plan.First.GoldOut,
            SecondGold = plan.Second.GoldOut,
            CompletedAt = time.GetUtcNow().UtcDateTime,
        };

        AddItems(record, record.FirstCharacterId, plan.First.Outgoing);
        AddItems(record, record.SecondCharacterId, plan.Second.Outgoing);
        return record;
    }

    private static void AddItems(TradeRecord record, CharacterId giver, IReadOnlyList<InventoryItem> outgoing)
    {
        foreach (InventoryItem item in outgoing)
        {
            record.Items.Add(new TradeRecordItem
            {
                TradeId = record.Id,
                ItemInstanceId = item.InstanceId,
                GiverCharacterId = giver,
                TemplateId = item.TemplateId,
                Count = item.Count,
            });
        }
    }

    private static TradeResult PartnerResult(TradeResult failing) => failing switch
    {
        TradeResult.BagFull => TradeResult.PartnerBagFull,
        TradeResult.GoldCapReached => TradeResult.PartnerGoldCapReached,
        TradeResult.UniqueAlreadyOwned => TradeResult.PartnerUniqueAlreadyOwned,
        _ => failing,
    };

    private static void TellSide(TradeSide side, TradeResult result, string? name) =>
        side.Connection.Send(STradeResultPacket.Create(result, name, PacketEncoder.Shared));

    /// <summary>1, 2, 4, 8, 16, then 30 seconds after each failed lookup.</summary>
    private static int LookupBackoffSeconds(int attempt) =>
        attempt >= 6 ? MaxLookupBackoffSeconds : Math.Min(1 << (attempt - 1), MaxLookupBackoffSeconds);

    private static string Describe(IReadOnlyList<InventoryItem> items) =>
        string.Join(", ", items.Select(i => $"{i.InstanceId.Value}:{i.TemplateId.Value}x{i.Count}"));

    private enum CompletionStep : byte
    {
        Committed,
        Refused,
        Unknown,
        LookupFailed,
    }

    /// <summary>What a save or a lookup learned, enqueued off the tick.</summary>
    private readonly record struct CompletionEvent(Completion Completion, CompletionStep Step, Exception? Error);

    /// <summary>One completing trade. Tick thread only (or SettleAsync once the tick has stopped).</summary>
    private sealed class Completion(Trade trade, TradePlan plan)
    {
        public Trade Trade { get; } = trade;

        public TradePlan Plan { get; } = plan;

        /// <summary>The save or a lookup is running: at most one at a time.</summary>
        public bool InFlight { get; set; }

        /// <summary>Completes once what is in flight has enqueued its result.</summary>
        public Task Pending { get; set; } = Task.CompletedTask;

        /// <summary>When the next record lookup may start; null while none is due.</summary>
        public DateTimeOffset? LookupDueAt { get; set; }

        public int LookupAttempts { get; set; }

        /// <summary>Applied or refused: anything later about it is ignored, so nothing applies twice.</summary>
        public bool Settled { get; set; }
    }
}
