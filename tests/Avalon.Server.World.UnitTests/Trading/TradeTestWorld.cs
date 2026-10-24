using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Trade;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Threading;
using Avalon.World.Trading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Trading;

/// <summary>
/// A TradeService over a manual clock and a saver the test drives, with characters brought online through connections
/// that record what they are sent. Every character starts in one instance, at the origin.
/// </summary>
internal sealed class TradeTestWorld
{
    public static readonly Guid Town = new("6f1b2a40-0000-4000-8000-000000000001");

    public static readonly ItemTemplate Contraband = new()
    { Id = new ItemTemplateId(9001), Name = "Contraband", Class = ItemClass.Junk, MaxStackSize = 1, Flags = ItemTemplateFlags.NoTrade };

    public static readonly ItemTemplate Token = new()
    { Id = new ItemTemplateId(9002), Name = "Token", Class = ItemClass.Quest, MaxStackSize = 1, Flags = ItemTemplateFlags.QuestItem };

    /// <param name="tickThread">Given, the service and who is online assert the tick thread with it.</param>
    public TradeTestWorld(Action<GameConfiguration>? configure = null, TickThreadGuard? tickThread = null)
    {
        configure?.Invoke(Config);
        Characters = new OnlineCharacters(tickThread);
        Economy = new TestEconomy(Config);
        Trades = new TradeService(Options.Create(Config), Clock, Saver, Characters, NullLogger<TradeService>.Instance,
            tickThread);
        Trades.Attach(Economy, Find, ItemModifierRepositories.EmptyCatalog);
    }

    public ManualTimerClock Clock { get; } = new();
    public GameConfiguration Config { get; } = new();
    /// <summary>Who is online: <see cref="Online(uint, Vector3?, Guid?, ulong)" /> adds each character.</summary>
    public OnlineCharacters Characters { get; }
    public FakeTradeSaver Saver { get; } = new();
    public TradeService Trades { get; }

    /// <summary>The economy the service completes through: one inventory per character, over <see cref="Find" />.</summary>
    public ICharacterEconomy Economy { get; }

    /// <summary>
    /// A trade service with no trade in it, for the containers that build every packet handler (the trade handlers take
    /// it) and never trade.
    /// </summary>
    public static TradeService InertService() =>
        new(Options.Create(new GameConfiguration()), TimeProvider.System, Substitute.For<ICharacterSaver>(),
            new OnlineCharacters(), NullLogger<TradeService>.Instance);

    /// <summary>The economy tests' catalogue, with <see cref="Contraband" /> and <see cref="Token" />.</summary>
    public static ItemTemplate? Find(ItemTemplateId id) =>
        id == Contraband.Id ? Contraband : id == Token.Id ? Token : TestCharacters.Find(id);

    public Trader Online(uint id, Vector3? at = null, Guid? instance = null, ulong gold = 0)
    {
        CharacterEntity character = TestCharacters.New(id, gold, Clock);
        character.InstanceId = instance ?? Town;
        character.Position = at ?? Vector3.zero;

        var sent = new List<OutboundPacket>();
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.AccountId.Returns(new AccountId(id));
        connection.When(c => c.Send(Arg.Any<OutboundPacket>())).Do(ci => sent.Add(ci.Arg<OutboundPacket>()));

        Characters.Add(connection);
        return new Trader(connection, character, sent);
    }

    /// <summary>Opens a trade between the two, <paramref name="sender" /> first, and clears what they were sent.</summary>
    public Trade Open(Trader sender, Trader target)
    {
        Assert.Equal(TradeResult.Ok, Trades.Request(sender.Id, target.Id));
        Assert.Equal(TradeResult.Ok, Trades.Respond(target.Id, accept: true));
        sender.Clear();
        target.Clear();
        return Trades.TradeOf(sender.Id)!;
    }

    private sealed class TestEconomy(GameConfiguration config) : ICharacterEconomy
    {
        private readonly Dictionary<CharacterEntity, IInventoryService> _inventories = [];

        public IInventoryService InventoryOf(CharacterEntity character)
        {
            if (!_inventories.TryGetValue(character, out IInventoryService? inventory))
            {
                inventory = TestCharacters.InventoryFor(character, Find);
                _inventories[character] = inventory;
            }

            return inventory;
        }

        public IWallet WalletOf(CharacterEntity character) => new CharacterWallet(character, config.MaxGold);

        public VendorTrade VendorOf(CharacterEntity character) => throw new NotSupportedException();
    }
}

internal sealed record Trader(IWorldConnection Connection, CharacterEntity Character, List<OutboundPacket> Sent)
{
    public uint Id => Character.Guid.Id;

    public List<T> Read<T>(NetworkPacketType type) => Sent
        .Where(p => p.Header.Type == type)
        .Select(p =>
        {
            using var stream = new MemoryStream(p.PayloadMemory.ToArray());
            return Serializer.Deserialize<T>(stream);
        })
        .ToList();

    public List<STradeRequestPacket> Requests() => Read<STradeRequestPacket>(NetworkPacketType.SMSG_TRADE_REQUEST);
    public List<STradeOpenedPacket> Opened() => Read<STradeOpenedPacket>(NetworkPacketType.SMSG_TRADE_OPENED);
    public List<STradeStatePacket> States() => Read<STradeStatePacket>(NetworkPacketType.SMSG_TRADE_STATE);
    public List<STradeClosedPacket> Closed() => Read<STradeClosedPacket>(NetworkPacketType.SMSG_TRADE_CLOSED);
    public List<STradeResultPacket> Results() => Read<STradeResultPacket>(NetworkPacketType.SMSG_TRADE_RESULT);

    public void Clear() => Sent.Clear();
}

/// <summary>
/// The trade half of <see cref="ICharacterSaver" />, driven by the test: each paired save and each record lookup waits
/// until the test finishes it, and what waits on it runs on the test's thread inside <see cref="Finish" /> or
/// <see cref="Answer" />, so the next Tick sees it. Every other member throws.
/// </summary>
internal sealed class FakeTradeSaver : ICharacterSaver
{
    private TaskCompletionSource<SaveOutcome>? _save;
    private TaskCompletionSource<bool>? _lookup;

    /// <summary>One entry per SaveTrade call.</summary>
    public List<(TradeSidePlan First, TradeSidePlan Second, TradeRecord Record)> Trades { get; } = [];

    /// <summary>Record lookups started.</summary>
    public int Lookups { get; private set; }

    /// <summary>One entry per ReleaseHold call.</summary>
    public List<CharacterId> Released { get; } = [];

    /// <summary>Evaluated at each release, into <see cref="ReleaseChecks" />.</summary>
    public Func<bool>? AtRelease { get; set; }

    public List<bool> ReleaseChecks { get; } = [];

    public Task<SaveOutcome> SaveTrade(
        IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character, TradeSidePlan Side)> sides,
        TradeRecord record)
    {
        Trades.Add((sides[0].Side, sides[1].Side, record));
        _save = new TaskCompletionSource<SaveOutcome>();
        return _save.Task;
    }

    /// <summary>Completes the pending paired save with <paramref name="outcome" />.</summary>
    public void Finish(SaveOutcome outcome) =>
        (_save ?? throw new InvalidOperationException("No paired save is pending.")).SetResult(outcome);

    public Task<bool> TradeCommittedAsync(Guid tradeId, AccountId first, AccountId second)
    {
        Lookups++;
        _lookup = new TaskCompletionSource<bool>();
        return _lookup.Task;
    }

    /// <summary>Completes the pending lookup with <paramref name="found" />, or faults it when null.</summary>
    public void Answer(bool? found)
    {
        TaskCompletionSource<bool> lookup = _lookup ?? throw new InvalidOperationException("No lookup is pending.");
        if (found is { } value)
            lookup.SetResult(value);
        else
            lookup.SetException(new InvalidOperationException("The lookup could not be answered."));
    }

    public void ReleaseHold(CharacterId id)
    {
        Released.Add(id);
        if (AtRelease is { } check)
            ReleaseChecks.Add(check());
    }

    public Task<bool> Save(IWorldConnection connection, CharacterEntity character) => throw new NotSupportedException();

    public Task<bool> Save(IReadOnlyList<(IWorldConnection Connection, CharacterEntity Character)> characters) =>
        throw new NotSupportedException();

    public Task<bool> SaveOnDespawnAsync(
        CharacterEntity character, Func<Character, CancellationToken, Task>? prepareRow,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task WhenIdle(CharacterId id) => throw new NotSupportedException();

    public Task WhenAllIdle() => throw new NotSupportedException();
}
