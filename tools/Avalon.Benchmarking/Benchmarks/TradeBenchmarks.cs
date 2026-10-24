using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Trade;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Testing.Fakes;
using Avalon.World.Trading;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// The trade service on the tick and per trade packet (#1175). <see cref="Tick" />: <c>TradeService.Tick</c>, which
/// World.Update calls every tick, with <see cref="OpenTrades" /> trades open and none closing: with none it reads two
/// counts and returns, and each open trade costs its automatic-cancel checks (both connections still hold their
/// characters, one instance, alive, out of combat, no NPC window, within 6 m), all of which allocate nothing.
/// <see cref="OfferAndRemove" />: one <c>CMSG_TRADE_OFFER_ITEM</c> and one <c>CMSG_TRADE_REMOVE_ITEM</c> through their
/// handlers, so the item goes into the trade and back out: each sends the whole trade to both sides and answers with
/// one <c>SMSG_TRADE_RESULT</c>, six packets in all, and those packets are all it allocates (the state packet reuses the
/// service's DTOs). It runs with <see cref="OpenTrades" /> open too, at least the one it uses, to show its cost does not
/// grow with them. The connections only count and release what they are sent.
/// </summary>
[MemoryDiagnoser]
public class TradeBenchmarks
{
    // The modifier catalog a state's names read: these potions roll none.
    private static readonly ItemModifierCatalog s_catalog = new([], [], NullLogger.Instance);

    private static readonly ItemTemplate s_potion = new()
    {
        Id = new ItemTemplateId(1),
        Name = "Potion",
        Class = ItemClass.Consumable,
        MaxStackSize = 20,
    };

    private readonly CTradeOfferItemPacket _offer = new() { BagSlot = 0, TradeSlot = 0 };
    private readonly CTradeRemoveItemPacket _remove = new() { TradeSlot = 0 };

    private TradeService _trades = null!;
    private QuietConnection _offerer = null!;
    private TradeOfferItemHandler _offerHandler = null!;
    private TradeRemoveItemHandler _removeHandler = null!;

    /// <summary>Trades open, none closing.</summary>
    [Params(0, 1, 100, 900)]
    public int OpenTrades { get; set; }

    [GlobalSetup(Target = nameof(Tick))]
    public void SetupTick()
    {
        Open(OpenTrades);

        // Checked by what it did: a tick that closed a trade measures something else.
        Tick();
        for (uint id = 1; id <= OpenTrades * 2; id++)
        {
            if (_trades.TradeOf(id) is null)
                throw new InvalidOperationException($"The tick closed the trade of character {id}.");
        }
    }

    [GlobalSetup(Target = nameof(OfferAndRemove))]
    public void SetupOfferAndRemove()
    {
        Open(Math.Max(OpenTrades, 1));
        ((CharacterEntity)_offerer.Character!).Container(InventoryType.Bag).Load(
            [new InventoryItem(0, new ItemInstanceId(Guid.CreateVersion7()), s_potion.Id, 5, 0, ItemInstanceFlags.None, ItemRarity.Common, 1, RareName: null, 0)]);
        _offerHandler = new TradeOfferItemHandler(_trades, NullLogger<TradeOfferItemHandler>.Instance);
        _removeHandler = new TradeRemoveItemHandler(_trades, NullLogger<TradeRemoveItemHandler>.Instance);

        // Checked by what it did: a refused offer or removal measures a refusal. Each sends the offerer the trade and
        // its result, and each moves the revision on (it opened at 1).
        int sentBefore = _offerer.Sent;
        OfferAndRemove();
        if (_offerer.Sent - sentBefore != 4 || _trades.TradeOf(1)!.Revision != 3)
            throw new InvalidOperationException("The offer and the removal were not each applied and answered once.");
    }

    [Benchmark]
    public void Tick() => _trades.Tick();

    [Benchmark]
    public void OfferAndRemove()
    {
        _offerHandler.Execute(_offerer, _offer);
        _removeHandler.Execute(_offerer, _remove);
    }

    /// <summary>
    /// <paramref name="trades" /> trades, each between two characters side by side at the origin, in no instance, so
    /// every one stays open on every tick; the first's first character is the offerer.
    /// </summary>
    private void Open(int trades)
    {
        var online = new OnlineCharacters();
        _trades = new TradeService(Options.Create(new GameConfiguration()), TimeProvider.System, NoSaves.Instance, online,
            NullLogger<TradeService>.Instance);
        // No trade completes here: the economy is only read by a completion.
        _trades.Attach(null!, static id => id == s_potion.Id ? s_potion : null, static () => s_catalog);

        var connections = new QuietConnection[Math.Max(trades, 1) * 2];
        for (int i = 0; i < connections.Length; i++)
        {
            connections[i] = new QuietConnection(NewCharacter((uint)i + 1));
            online.Add(connections[i]);
        }

        for (int i = 0; i < trades * 2; i += 2)
        {
            if (_trades.Request((uint)i + 1, (uint)i + 2) != TradeResult.Ok
                || _trades.Respond((uint)i + 2, accept: true) != TradeResult.Ok)
            {
                throw new InvalidOperationException($"Trade {i / 2} did not open.");
            }
        }

        _offerer = connections[0];
    }

    private static CharacterEntity NewCharacter(uint id)
    {
        var row = new Character
        {
            Id = new CharacterId(id),
            AccountId = new AccountId(id),
            Name = $"Trader{id}",
            Class = CharacterClass.Warrior,
            CreationDate = DateTime.UtcNow,
        };
        return new CharacterEntity(NullLoggerFactory.Instance, row, new RegenConfiguration()) { Data = row };
    }

    /// <summary>No trade completes here, so nothing is ever saved.</summary>
    private sealed class NoSaves : ICharacterSaver
    {
        public static readonly NoSaves Instance = new();

        public Task<bool> Save(IWorldConnection connection, CharacterEntity character) => throw new NotSupportedException();

        public Task<bool> Save(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters) =>
            throw new NotSupportedException();

        public Task<bool> SaveOnDespawnAsync(CharacterEntity character, Func<Character, CancellationToken, Task>? prepareRow,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SaveOutcome> SaveTrade(
            IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character, TradeSidePlan Side)> sides,
            TradeRecord record) => throw new NotSupportedException();

        public Task<bool> TradeCommittedAsync(Guid tradeId, AccountId first, AccountId second) =>
            throw new NotSupportedException();

        public void ReleaseHold(CharacterId id) => throw new NotSupportedException();

        public Task WhenIdle(CharacterId id) => throw new NotSupportedException();

        public Task WhenAllIdle() => throw new NotSupportedException();
    }
}
