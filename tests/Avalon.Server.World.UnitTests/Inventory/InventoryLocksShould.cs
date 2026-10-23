using Avalon.Common;
using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public.Characters;
using static Avalon.Server.World.UnitTests.Inventory.EquipTemplates;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// An item offered in a trade is locked (#1175): nothing may move, split, merge into, swap with, bank, equip or destroy
/// it, adds never fill its stack, removals by template never see it, and gold held for the trade cannot be spent. While
/// the trade completes, what it will add is reserved.
/// </summary>
public class InventoryLocksShould
{
    private readonly CharacterEntity _character = New(7, gold: 100);
    private readonly InventoryItem _lockedPotion = Item(0, Potion, count: 5);
    private readonly InventoryItem _lockedSword = Item(3, Sword);

    public InventoryLocksShould()
    {
        _character.Container(InventoryType.Bag).Load([_lockedPotion, Item(1, Sword), Item(2, Potion, count: 3), _lockedSword]);
        _character.Locks.Lock(_lockedPotion.InstanceId);
        _character.Locks.Lock(_lockedSword.InstanceId);
    }

    [Theory]
    [InlineData("move a locked item")]
    [InlineData("split a locked stack")]
    [InlineData("merge into a locked stack")]
    [InlineData("swap onto a locked item")]
    [InlineData("bank a locked item")]
    [InlineData("equip a locked item by drag")]
    [InlineData("destroy a locked item")]
    public void Refuse_every_request_that_would_touch_a_locked_item(string request)
    {
        CharacterInventoryService inventory = TestCharacters.InventoryFor(_character);

        ItemRequestResult result = request switch
        {
            "move a locked item" => inventory.TryMove(Bag(0), Bag(9), null, false),
            "split a locked stack" => inventory.TryMove(Bag(0), Bag(9), 2, false),
            "merge into a locked stack" => inventory.TryMove(Bag(2), Bag(0), null, false),
            "swap onto a locked item" => inventory.TryMove(Bag(1), Bag(0), null, false),
            "bank a locked item" => inventory.TryMove(Bag(0), Vault(0), null, bankAccessible: true),
            "equip a locked item by drag" => inventory.TryMove(Bag(3), Eq(EquipmentSlots.MainHand), null, false),
            "destroy a locked item" => inventory.TryDestroy(Bag(0), null, false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request, null),
        };

        Assert.Equal(ItemRequestResult.Locked, result);
        Assert.False(_character.SaveState.HasChanges);
    }

    [Fact]
    public void Leave_locked_stacks_and_held_gold_out_of_adds_removals_and_spending()
    {
        CharacterInventoryService inventory = TestCharacters.InventoryFor(_character);
        var wallet = new CharacterWallet(_character, maxGold: 1_000);

        Assert.Equal(InventoryAddResult.Ok, inventory.TryAdd(Potion.Id, 4));
        Assert.Equal(7u, At(_character, InventoryType.Bag, 2).Count);
        Assert.Equal(5u, At(_character, InventoryType.Bag, 0).Count);
        Assert.Equal(InventoryRemoveResult.NotEnough, inventory.TryRemove(Potion.Id, 10));
        Assert.Equal(InventoryRemoveResult.Locked, inventory.TryRemove(_lockedPotion.InstanceId, 1));
        Assert.Equal(5u, At(_character, InventoryType.Bag, 0).Count);

        _character.Locks.HeldGold = 60;
        Assert.Equal(WalletResult.NotEnoughGold, wallet.CanSpend(41));
        Assert.Equal(WalletResult.Ok, wallet.TrySpend(40));
        Assert.Equal(60UL, wallet.Balance);
    }

    /// <summary>
    /// While a trade completes, the receiver's destination slots, incoming gold and incoming Unique copies are spoken
    /// for: every add but the trade's own treats them as taken.
    /// </summary>
    [Fact]
    public void Keep_a_completing_trade_s_reservations_out_of_every_other_add()
    {
        CharacterEntity character = New(8, gold: 40);
        character.Container(InventoryType.Bag).Load(Enumerable.Range(0, 28).Select(s => Item((byte)s, Sword)).ToList());
        CharacterInventoryService inventory = TestCharacters.InventoryFor(character);
        var wallet = new CharacterWallet(character, maxGold: 100);
        character.Locks.Reserve(28);

        Assert.Equal(InventoryAddResult.Ok, inventory.TryAdd(Potion.Id, 20));
        Assert.Equal(Potion.Id, At(character, InventoryType.Bag, 29).TemplateId);
        Assert.Equal(InventoryAddResult.InventoryFull, inventory.CanAdd(Potion.Id, 1));
        Assert.Equal(InventoryAddResult.InventoryFull, inventory.TryAddInstance(TestCharacters.Carried(Item(0, Sword))));
        Assert.Equal(ItemRequestResult.Locked, inventory.TryMove(Bag(0), Bag(28), null, false));
        character.Locks.ExpectIncoming(Relic.Id, 1);
        Assert.Equal(InventoryAddResult.UniqueAlreadyOwned, inventory.TryAdd(Relic.Id, 1));
        character.Locks.IncomingGold = 50;
        Assert.Equal(WalletResult.GoldCapReached, wallet.TryAddGold(11));
        Assert.Equal(WalletResult.Ok, wallet.TryAddGold(10));
        Assert.False(character.Container(InventoryType.Bag).TryGet(28, out _));

        character.Locks.Clear();
        InventoryItem traded = Item(0, Sword);
        Assert.Equal(InventoryAddResult.Ok, inventory.TryAddInstanceAt(TestCharacters.Carried(traded), 28));
        Assert.Equal(traded.InstanceId, At(character, InventoryType.Bag, 28).InstanceId);
    }
}
