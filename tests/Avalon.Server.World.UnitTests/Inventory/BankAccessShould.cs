using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// When the bank is open (spec #463). The spec's cases (no conversation, not a banker, outside the
/// leash, inside it) are here, because InventoryMove takes the answer as an input.
/// </summary>
public class BankAccessShould
{
    [Fact]
    public async Task Be_closed_with_no_conversation()
    {
        var w = await BankerWorld.CreateAsync();
        w.Character.OpenBankNpc = BankerWorld.BankerGuid;

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Empty(w.Sent);
    }

    [Fact]
    public async Task Be_closed_in_a_banker_conversation_that_never_opened_the_bank()
    {
        var w = await BankerWorld.CreateAsync();
        w.Connection.CurrentDialogue = (BankerWorld.BankerGuid, new DialogueNodeId(BankerWorld.BankerRoot));

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.NotNull(w.Connection.CurrentDialogue);
    }

    [Fact]
    public async Task Be_closed_in_a_conversation_with_someone_else()
    {
        var w = await BankerWorld.CreateAsync();
        w.Character.OpenBankNpc = BankerWorld.BankerGuid;
        w.Connection.CurrentDialogue = (BankerWorld.StrangerGuid, new DialogueNodeId(BankerWorld.StrangerRoot));

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
    }

    [Fact]
    public async Task Be_usable_inside_the_leash()
    {
        var w = await BankerWorld.CreateAsync();
        w.OpenBank();

        Assert.True(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.True(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Empty(w.Sent);
    }

    [Fact]
    public async Task Be_usable_at_exactly_the_leash()
    {
        var w = await BankerWorld.CreateAsync();
        w.OpenBank();
        w.Character.Position = new Vector3(0, 0, 3 + 15);

        Assert.True(BankAccess.TryUse(w.Connection, w.Character, w.World));
    }

    [Fact]
    public async Task End_the_conversation_past_the_leash()
    {
        var w = await BankerWorld.CreateAsync();
        w.OpenBank();
        w.Character.Position = new Vector3(0, 0, 19);

        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));

        Assert.Null(w.Connection.CurrentDialogue);
        Assert.Null(w.Character.OpenBankNpc);
        SDialogueEndPacket end = Assert.Single(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Equal(BankerWorld.BankerGuid.RawValue, end.SpeakerGuid);
    }

    [Fact]
    public async Task End_the_conversation_with_a_creature_that_is_not_a_banker()
    {
        var w = await BankerWorld.CreateAsync();
        w.Connection.CurrentDialogue = (BankerWorld.StrangerGuid, new DialogueNodeId(BankerWorld.StrangerRoot));
        w.Character.OpenBankNpc = BankerWorld.StrangerGuid;

        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Null(w.Connection.CurrentDialogue);
    }

    [Fact]
    public async Task End_the_conversation_when_the_banker_has_left_the_instance()
    {
        var w = await BankerWorld.CreateAsync();
        w.OpenBank();
        w.Creatures.Remove(BankerWorld.BankerGuid);

        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Null(w.Connection.CurrentDialogue);
    }

    [Fact]
    public async Task End_the_conversation_when_the_banker_is_dead()
    {
        var w = await BankerWorld.CreateAsync();
        w.OpenBank();
        w.Banker.CurrentHealth.Returns(0u);

        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Null(w.Connection.CurrentDialogue);
    }

    [Fact]
    public async Task Describe_every_bank_slot_in_one_snapshot()
    {
        var w = await BankerWorld.CreateAsync();
        w.Character.Container(InventoryType.Bank).Load([Item(2, Potion, count: 7)]);

        InventorySlotUpdateDto[] snapshot = BankAccess.Snapshot(w.Character);

        Assert.Equal(30, snapshot.Length);
        Assert.All(snapshot, s => Assert.Equal((ushort)InventoryType.Bank, s.Container));
        Assert.Equal(Enumerable.Range(0, 30).Select(i => (ushort)i), snapshot.Select(s => s.Slot));
        Assert.Equal(7u, snapshot[2].Item!.Count);
        Assert.Equal(29, snapshot.Count(s => s.Item is null));
    }
}
