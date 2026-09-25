using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// The vendor-buy pattern the spec prescribes: check CanAdd and CanSpend, then apply TrySpend and
/// TryAdd, which can no longer fail. Single-threaded and in memory, so there is nothing in between.
/// </summary>
public class CompositeBuyShould
{
    private static bool Buy(CharacterEntity character, ulong price)
    {
        IInventoryService inventory = InventoryFor(character);
        var wallet = new CharacterWallet(character, maxMoney: 1_000_000);

        if (inventory.CanAdd(Potion.Id, 1) != InventoryAddResult.Ok || wallet.CanSpend(price) != WalletResult.Ok)
            return false;

        Assert.Equal(WalletResult.Ok, wallet.TrySpend(price));
        Assert.Equal(InventoryAddResult.Ok, inventory.TryAdd(Potion.Id, 1));
        return true;
    }

    [Fact]
    public void Leave_no_partial_state_when_the_bag_is_full()
    {
        CharacterEntity character = New(money: 50);
        character.Container(InventoryType.Bag).Load(Enumerable.Range(0, 30).Select(s => Item((ushort)s, Sword)).ToList());

        Assert.False(Buy(character, 30));

        Assert.Equal(50UL, character.Data!.Money);
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Leave_no_partial_state_when_the_money_is_short()
    {
        CharacterEntity character = New(money: 20);

        Assert.False(Buy(character, 30));

        Assert.Empty(character.Container(InventoryType.Bag).Items);
        Assert.False(character.SaveState.HasChanges);
    }

    [Fact]
    public void Take_the_money_and_give_the_item_together()
    {
        CharacterEntity character = New(money: 50);

        Assert.True(Buy(character, 30));

        Assert.Equal(20UL, character.Data!.Money);
        Assert.Single(character.Container(InventoryType.Bag).Items);
        Assert.True(character.SaveState.MoneyDirty);
    }
}
