using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Vendor;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using Avalon.World.Vendors;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A Collect objective counts the quest item in the bag (#433), however it got there: recounted at accept, at a
/// stage start and, through QuestFlusher, on every tick the bag changed. Abandon takes every copy back.
/// </summary>
public class QuestCollectShould
{
    private static readonly ItemTemplateId TuskId = new(Tusk);

    private static async Task<(QuestTestWorld W, QuestClient C)> WithTusksReadyToAcceptAsync()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        QuestTestWorld.Complete(c, Hunt);
        return (w, c);
    }

    private static void Flush(QuestTestWorld w, QuestClient c)
    {
        QuestFlusher.Flush(c.Connection, w.Quests);
        InventoryUpdateFlusher.Flush(c.Connection);   // as WorldServer does next, clearing the tick's slot changes
    }

    [Fact]
    public async Task Count_tusks_already_in_the_bag_at_accept()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        Assert.Equal(InventoryAddResult.Ok, w.Economy.InventoryOf(c.Character).TryAdd(TuskId, 1));

        w.Accept(c, Tusks);

        Assert.Equal(1u, c.Character.Quests.Get(Tusks)!.ProgressOf(TusksCollect));
    }

    [Fact]
    public async Task Count_a_tusk_that_arrives_later_and_become_ready()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        w.Accept(c, Tusks);

        w.Economy.InventoryOf(c.Character).TryAdd(TuskId, 2);
        Flush(w, c);

        Assert.Equal(2u, c.Character.Quests.Get(Tusks)!.ProgressOf(TusksCollect));
        Assert.Equal(CharacterQuestState.ReadyToTurnIn, c.Character.Quests.Get(Tusks)!.State);
    }

    /// <summary>Review Focus 1.</summary>
    [Fact]
    public async Task Lower_the_count_and_fall_back_to_active_when_an_item_is_destroyed()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        w.Accept(c, Tusks);
        w.Economy.InventoryOf(c.Character).TryAdd(TuskId, 2);
        Flush(w, c);

        ushort slot = c.Character.Container(InventoryType.Bag).Items.Single().Slot;
        w.Economy.InventoryOf(c.Character).TryDestroy(new SlotRef(InventoryType.Bag, slot), 1, bankAccessible: false);
        Flush(w, c);

        Assert.Equal(1u, c.Character.Quests.Get(Tusks)!.ProgressOf(TusksCollect));
        Assert.Equal(CharacterQuestState.Active, c.Character.Quests.Get(Tusks)!.State);
    }

    [Fact]
    public async Task Stop_counting_a_tusk_moved_to_the_bank()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        w.Accept(c, Tusks);
        w.Economy.InventoryOf(c.Character).TryAdd(TuskId, 1);
        Flush(w, c);

        ushort slot = c.Character.Container(InventoryType.Bag).Items.Single().Slot;
        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            w.Economy.InventoryOf(c.Character).TryMove(new SlotRef(InventoryType.Bag, slot), new SlotRef(InventoryType.Bank, 0), null, bankAccessible: true));
        Flush(w, c);

        Assert.Equal(0u, c.Character.Quests.Get(Tusks)!.ProgressOf(TusksCollect));
    }

    /// <summary>
    /// Both stacks are loaded as select loads them (unmarked), so the removal is seen through the service's own marks:
    /// each item is saved as removed and each slot is owed to the client.
    /// </summary>
    [Fact]
    public async Task Take_every_copy_back_from_the_bag_and_the_bank_on_abandon()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        w.Accept(c, Tusks);
        InventoryItem inBag = TestCharacters.Item(4, TuskItem(), 3);
        InventoryItem inBank = TestCharacters.Item(0, TuskItem(), 1);
        c.Character.Container(InventoryType.Bag).Load([inBag]);
        c.Character.Container(InventoryType.Bank).Load([inBank]);
        c.Character.ClientChanges.Clear();

        w.Quests.Abandon(c.Character, Tusks);

        Assert.DoesNotContain(c.Character.Container(InventoryType.Bag).Items, i => i.TemplateId == TuskId);
        Assert.DoesNotContain(c.Character.Container(InventoryType.Bank).Items, i => i.TemplateId == TuskId);
        Assert.Equal(SaveState.Removed, c.Character.SaveState.ItemState(inBag.InstanceId));
        Assert.Equal(SaveState.Removed, c.Character.SaveState.ItemState(inBank.InstanceId));
        Assert.Contains((InventoryType.Bag, (ushort)4), c.Character.ClientChanges.Slots);
        Assert.Contains((InventoryType.Bank, (ushort)0), c.Character.ClientChanges.Slots);
    }

    /// <summary>Review fix: a tick whose only changes were money and the Bank leaves every Collect count alone.</summary>
    [Fact]
    public async Task Recount_nothing_when_only_money_or_the_bank_changed()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        w.Accept(c, Tusks);
        ActiveQuest quest = c.Character.Quests.Get(Tusks)!;
        c.Character.Quests.SetProgress(quest, TusksCollect, 1);   // the bag holds none: a recount would set 0
        c.Character.Container(InventoryType.Bank).Load([TestCharacters.Item(0, TuskItem(), 1)]);
        c.Character.ClientChanges.Clear();

        Assert.Equal(WalletResult.Ok, w.Economy.WalletOf(c.Character).TryAddMoney(5));
        Assert.Equal(Avalon.Network.Packets.Character.ItemRequestResult.Ok,
            w.Economy.InventoryOf(c.Character).TryDestroy(new SlotRef(InventoryType.Bank, 0), 1, bankAccessible: true));
        Assert.True(c.Character.ClientChanges.MoneyChanged);
        Assert.DoesNotContain(c.Character.ClientChanges.Slots, s => s.Container == InventoryType.Bag);
        Flush(w, c);

        Assert.Equal(1u, quest.ProgressOf(TusksCollect));
    }

    /// <summary>Review fix: a Collect count comes only from the Bag; AddProgress refuses it and changes nothing.</summary>
    [Fact]
    public async Task Refuse_progress_added_to_a_collect_objective()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        w.Accept(c, Tusks);

        Assert.False(w.Quests.AddProgress(c.Character, Tusks, TusksCollect, 2));

        Assert.Equal(0u, c.Character.Quests.Get(Tusks)!.ProgressOf(TusksCollect));
        Assert.Equal(CharacterQuestState.Active, c.Character.Quests.Get(Tusks)!.State);
    }

    [Fact]
    public async Task Refuse_to_sell_a_quest_item()
    {
        (QuestTestWorld w, QuestClient c) = await WithTusksReadyToAcceptAsync();
        ItemTemplate sellable = TuskItem();
        sellable.Flags = ItemTemplateFlags.QuestItem;   // not NoSell: the QuestItem flag alone refuses it
        sellable.SellPrice = 5;
        c.Character.Container(InventoryType.Bag).Load([TestCharacters.Item(0, sellable, 1)]);

        SellDecision decision = VendorRules.DecideSell(c.Character, shopOpen: true, bagSlot: 0, count: 1, _ => sellable, maxMoney: 1_000_000);

        Assert.Equal(VendorResult.NotSellable, decision.Result);
    }
}
