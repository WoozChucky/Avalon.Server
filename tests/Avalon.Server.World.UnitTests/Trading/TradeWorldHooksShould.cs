using Avalon.Common;
using Avalon.Network.Packets.Trade;
using Avalon.Server.World.UnitTests.Inventory;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Trading;

/// <summary>
/// The world's trade hooks (#1175): a real World holding the trade service closes an open trade when a trader moves to
/// another instance or leaves the world, and every lock it held goes with it.
/// </summary>
public class TradeWorldHooksShould
{
    [Theory]
    [InlineData("transfer")]
    [InlineData("leave")]
    public async Task Close_an_open_trade_when_a_trader_leaves_its_instance(string how)
    {
        var w = new TradeTestWorld();
        Avalon.World.World world = await BankInstanceChangeShould.RealWorldAsync(w.Trades, w.Economy, [Sword]);
        Trader a = w.Online(1);
        Trader b = w.Online(2);
        a.Character.Container(InventoryType.Bag).Load([Item(0, Sword)]);
        w.Open(a, b);
        Assert.Equal(TradeResult.Ok, w.Trades.OfferItem(a.Id, bagSlot: 0, tradeSlot: 0));
        Assert.True(a.Character.Locks.Any);

        if (how == "transfer")
            world.TransferPlayer(a.Connection, BankInstanceChangeShould.Elsewhere());
        else
            await world.LeaveWorldAsync(a.Connection);

        Assert.Equal(TradeResult.Left, b.Closed().Single().Result);
        // The trader who moved is told so; one who left the world has no window left to tell.
        Assert.Equal(how == "transfer" ? [TradeResult.LeftInstance] : [], a.Closed().Select(p => p.Result));
        Assert.False(a.Character.Locks.Any);
        Assert.Null(w.Trades.TradeOf(b.Id));
    }
}
