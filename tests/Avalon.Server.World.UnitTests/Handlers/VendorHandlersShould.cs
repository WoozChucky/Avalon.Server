using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Vendors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Vendors.VendorTestData;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// CMSG_VENDOR_BUY, CMSG_VENDOR_SELL and CMSG_VENDOR_BUYBACK on the tick (spec #432). Every request
/// gets exactly one SMSG_VENDOR_RESULT, a refusal changes nothing, and nothing throws onto the
/// tick. The rules themselves are VendorRulesShould's.
/// </summary>
public class VendorHandlersShould : IAsyncLifetime
{
    private VendorWorld _w = null!;

    public async Task InitializeAsync() => _w = await VendorWorld.CreateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private CharacterEntity Me => _w.Main.Character;

    [Fact]
    public void Answer_every_request_with_exactly_one_result()
    {
        _w.OpenShop();

        _w.Buy(_w.Main, 1, TonicSequence);
        _w.Sell(_w.Main, 2, 0);
        _w.Buyback(_w.Main, 3, 0);

        List<SVendorResultPacket> results = _w.Main.Results();
        Assert.Equal([1u, 2u, 3u], results.Select(r => r.RequestId));
        Assert.All(results, r => Assert.Equal(VendorResult.Ok, r.Result));
        Assert.Equal(1000UL - 10 + 4 - 4, Me.Data!.Money);
    }

    [Fact]
    public void Answer_Dead_to_a_dead_character_without_ending_the_conversation()
    {
        _w.OpenShop();
        Me.Container(InventoryType.Bag).Load([TestCharacters.Item(0, Tonic, count: 5)]);
        Me.IsDead = true;

        Assert.Equal(VendorResult.Dead, _w.Buy(_w.Main, 1, TonicSequence));
        Assert.Equal(VendorResult.Dead, _w.Sell(_w.Main, 2, 0));
        Assert.Equal(VendorResult.Dead, _w.Buyback(_w.Main, 3, 0));

        Assert.Empty(_w.Main.Ends());
        Assert.True(ShopAccess.IsOpen(_w.Main.Connection, Me));
        Assert.Equal(1000UL, Me.Data!.Money);
        Assert.False(Me.ClientChanges.HasChanges);
    }

    [Fact]
    public void Answer_ShopClosed_with_no_shop_open()
    {
        Me.Container(InventoryType.Bag).Load([TestCharacters.Item(0, Tonic, count: 5)]);

        Assert.Equal(VendorResult.ShopClosed, _w.Buy(_w.Main, 1, TonicSequence));
        Assert.Equal(VendorResult.ShopClosed, _w.Sell(_w.Main, 2, 0));
        Assert.Equal(VendorResult.ShopClosed, _w.Buyback(_w.Main, 3, 0));

        Assert.Equal(3, _w.Main.Results().Count);
        Assert.Empty(_w.Main.Ends());
        Assert.Equal(5u, TestCharacters.At(Me, InventoryType.Bag, 0).Count);
    }

    [Fact]
    public void Refuse_every_request_past_the_leash_and_end_the_conversation_once()
    {
        _w.OpenShop();
        Me.Position = new Vector3(0, 0, 25);

        Assert.Equal(VendorResult.ShopClosed, _w.Buy(_w.Main, 1, TonicSequence));
        Assert.Equal(VendorResult.ShopClosed, _w.Sell(_w.Main, 2, 0));

        Assert.Equal(VendorWorld.SmithGuid.RawValue, Assert.Single(_w.Main.Ends()).SpeakerGuid);
        Assert.Null(_w.Main.Connection.CurrentDialogue);
    }

    /// <summary>Garbage off the wire: each gets one refusal, never an exception.</summary>
    [Fact]
    public void Answer_malformed_requests_with_one_refusal_each()
    {
        _w.OpenShop();

        Assert.Equal(VendorResult.NotFound, _w.Buy(_w.Main, 1, 99));
        Assert.Equal(VendorResult.InvalidCount, _w.Buy(_w.Main, 2, TonicSequence, 0));
        Assert.Equal(VendorResult.NotFound, _w.Sell(_w.Main, 3, 70000));
        Assert.Equal(VendorResult.NotFound, _w.Buyback(_w.Main, 4, uint.MaxValue));

        Assert.Equal(4, _w.Main.Results().Count);
        Assert.Equal(1000UL, Me.Data!.Money);
        Assert.False(Me.ClientChanges.HasChanges);
        Assert.False(Me.SaveState.HasChanges);
    }

    /// <summary>Two buys of the last unit in one tick: the second sees what the first left.</summary>
    [Fact]
    public void Refuse_the_second_buy_of_the_last_unit_in_one_tick()
    {
        VendorWorld.Shopper rival = _w.AddShopper(8, money: 1000);
        _w.OpenShop();
        _w.OpenShop(rival);
        Assert.True(_w.Stocks.TryGet(VendorWorld.SmithGuid, out VendorStockState? stock));
        VendorStockView blade = stock.Rows.Single(r => r.Sequence == BladeSequence);
        stock.Take(blade, 1, Now);   // one left

        Assert.Equal(VendorResult.Ok, _w.Buy(_w.Main, 1, BladeSequence));
        Assert.Equal(VendorResult.OutOfStock, _w.Buy(rival, 1, BladeSequence));

        Assert.Equal(0u, stock.Available(blade));
        Assert.Equal((900UL, 1000UL), (Me.Data!.Money, rival.Character.Data!.Money));
        Assert.Single(Me.Container(InventoryType.Bag).Items);
        Assert.Empty(rival.Character.Container(InventoryType.Bag).Items);
    }

    [Fact]
    public void Answer_NotFound_when_the_trade_throws()
    {
        _w.OpenShop();
        var economy = Substitute.For<ICharacterEconomy>();
        economy.VendorOf(Arg.Any<CharacterEntity>()).Returns(_ => throw new InvalidOperationException("boom"));
        _w.Economy = economy;

        Assert.Equal(VendorResult.NotFound, _w.Buy(_w.Main, 1, TonicSequence));
        Assert.Equal(VendorResult.NotFound, _w.Sell(_w.Main, 2, 0));
        Assert.Equal(VendorResult.NotFound, _w.Buyback(_w.Main, 3, 0));

        Assert.Equal(3, _w.Main.Results().Count);
    }

    /// <summary>
    /// The rules accept each request, then applying it fails, so VendorTrade throws rather than
    /// carry on. The handler logs that at Error and still answers once, with NotFound.
    /// </summary>
    [Fact]
    public void Log_an_Error_and_answer_NotFound_when_an_accepted_trade_fails_to_apply()
    {
        _w.OpenShop();
        Me.Container(InventoryType.Bag).Load([TestCharacters.Item(0, Tonic, count: 5)]);
        Me.Buyback.Push(new BuybackEntry(TestCharacters.Item(3, Tonic, count: 1), 4));

        var inventory = Substitute.For<IInventoryService>();
        inventory.TryAdd(Arg.Any<ItemTemplateId>(), Arg.Any<uint>()).Returns(InventoryAddResult.InventoryFull);
        inventory.TryAddInstance(Arg.Any<InventoryItem>()).Returns(InventoryAddResult.InventoryFull);
        inventory.TakeOut(Arg.Any<SlotRef>(), Arg.Any<uint>()).Returns(_ => throw new InvalidOperationException("boom"));
        IWallet wallet = new CharacterEconomy(_w.World, new ItemIdAllocator()).WalletOf(Me);
        var economy = Substitute.For<ICharacterEconomy>();
        economy.VendorOf(Me).Returns(_ => new VendorTrade(Me, inventory, wallet, Find, 1_000_000));

        var buyLog = new LevelLogger<VendorBuyHandler>();
        var sellLog = new LevelLogger<VendorSellHandler>();
        var buybackLog = new LevelLogger<VendorBuybackHandler>();
        IWorldConnection connection = _w.Main.Connection;

        new VendorBuyHandler(buyLog, _w.World, economy, _w.Quests, _w.Clock)
            .Execute(connection, new CVendorBuyPacket { RequestId = 1, Sequence = TonicSequence });
        new VendorSellHandler(sellLog, _w.World, economy)
            .Execute(connection, new CVendorSellPacket { RequestId = 2, BagSlot = 0 });
        new VendorBuybackHandler(buybackLog, _w.World, economy)
            .Execute(connection, new CVendorBuybackPacket { RequestId = 3, Index = 0 });

        List<SVendorResultPacket> results = _w.Main.Results();
        Assert.Equal([1u, 2u, 3u], results.Select(r => r.RequestId));
        Assert.All(results, r => Assert.Equal(VendorResult.NotFound, r.Result));
        Assert.Equal([LogLevel.Error], buyLog.Levels);
        Assert.Equal([LogLevel.Error], sellLog.Levels);
        Assert.Equal([LogLevel.Error], buybackLog.Levels);
    }

    /// <summary>The map filter lets no such request through, and there is no one to answer: nothing is sent.</summary>
    [Fact]
    public void Send_nothing_to_a_connection_with_no_character()
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns((ICharacter?)null);

        new VendorBuyHandler(NullLogger<VendorBuyHandler>.Instance, _w.World, _w.Economy, _w.Quests, _w.Clock)
            .Execute(connection, new CVendorBuyPacket { RequestId = 1, Sequence = TonicSequence });
        new VendorSellHandler(NullLogger<VendorSellHandler>.Instance, _w.World, _w.Economy)
            .Execute(connection, new CVendorSellPacket { RequestId = 2, BagSlot = 0 });
        new VendorBuybackHandler(NullLogger<VendorBuybackHandler>.Instance, _w.World, _w.Economy)
            .Execute(connection, new CVendorBuybackPacket { RequestId = 3, Index = 0 });

        connection.DidNotReceive().Send(Arg.Any<NetworkPacket>());
    }

    /// <summary>
    /// A sale starts the restock timer at the handler's TimeProvider, the clock the vendor pass
    /// restocks by, never at the system clock. With the clock ten years ahead, a sale timed by the
    /// system clock would already be long due at the first pass.
    /// </summary>
    [Fact]
    public void Start_the_restock_timer_at_the_handler_clock()
    {
        _w.Clock.Now = _w.Clock.Now.AddYears(10);
        _w.OpenShop();

        Assert.Equal(VendorResult.Ok, _w.Buy(_w.Main, 1, BladeSequence));
        Assert.True(_w.Stocks.TryGet(VendorWorld.SmithGuid, out VendorStockState? stock));
        VendorStockView blade = stock.Rows.Single(r => r.Sequence == BladeSequence);

        _w.Clock.Now = _w.Clock.Now.AddSeconds(59);
        _w.EndOfTick();
        Assert.Equal(1u, stock.Available(blade));

        _w.Clock.Now = _w.Clock.Now.AddSeconds(1);
        _w.EndOfTick();
        Assert.Equal(2u, stock.Available(blade));
    }

    private sealed class LevelLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Information)
                Levels.Add(logLevel);
        }
    }
}
