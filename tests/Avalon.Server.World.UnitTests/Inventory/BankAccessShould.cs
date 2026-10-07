using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.World.Dialogue;
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
    /// <summary>The bank is open only in the conversation that opened it; a closed bank leaves the conversation alone.</summary>
    [Theory]
    [InlineData("no conversation")]
    [InlineData("a banker conversation that never opened the bank")]
    [InlineData("a conversation with someone else")]
    public async Task Stay_closed_outside_the_conversation_that_opened_it(string state)
    {
        BankerWorld w = await BankerWorld.CreateAsync();
        switch (state)
        {
            case "no conversation":
                w.Character.OpenBankNpc = BankerWorld.BankerGuid;
                break;
            case "a banker conversation that never opened the bank":
                w.Connection.CurrentDialogue = (BankerWorld.BankerGuid, new DialogueNodeId(BankerWorld.BankerRoot));
                break;
            default:
                w.Character.OpenBankNpc = BankerWorld.BankerGuid;
                w.Connection.CurrentDialogue = (BankerWorld.StrangerGuid, new DialogueNodeId(BankerWorld.StrangerRoot));
                break;
        }

        (ObjectGuid Npc, DialogueNodeId Node)? conversation = w.Connection.CurrentDialogue;

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Equal(conversation, w.Connection.CurrentDialogue);
        Assert.Empty(w.Sent);
    }

    /// <summary>5.5 m from the banker is too far to open a conversation, close enough to keep one; the leash is inclusive.</summary>
    [Theory]
    [InlineData(5.5f)]
    [InlineData(NpcInteraction.LeashRange)]
    public async Task Be_usable_anywhere_inside_the_leash(float distance)
    {
        BankerWorld w = await BankerWorld.CreateAsync();
        w.OpenBank();
        w.Character.Position = new Vector3(0, 0, 3 + distance);

        Assert.True(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.True(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Empty(w.Sent);
    }

    [Theory]
    [InlineData("past the leash")]
    [InlineData("not a banker")]
    [InlineData("gone from the instance")]
    [InlineData("dead")]
    public async Task End_the_conversation_when_the_banker_can_no_longer_serve(string why)
    {
        BankerWorld w = await BankerWorld.CreateAsync();
        w.OpenBank();
        ObjectGuid npc = BankerWorld.BankerGuid;
        switch (why)
        {
            case "past the leash":
                w.Character.Position = new Vector3(0, 0, 3 + NpcInteraction.LeashRange + 0.01f);
                break;
            case "not a banker":
                npc = BankerWorld.StrangerGuid;
                w.Connection.CurrentDialogue = (npc, new DialogueNodeId(BankerWorld.StrangerRoot));
                w.Character.OpenBankNpc = npc;
                break;
            case "gone from the instance":
                w.Creatures.Remove(BankerWorld.BankerGuid);
                break;
            default:
                w.Banker.CurrentHealth.Returns(0u);
                break;
        }

        Assert.False(BankAccess.TryUse(w.Connection, w.Character, w.World));

        Assert.Null(w.Connection.CurrentDialogue);
        Assert.Null(w.Character.OpenBankNpc);
        SDialogueEndPacket end = Assert.Single(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Equal(npc.RawValue, end.SpeakerGuid);
    }

    [Fact]
    public async Task Describe_every_bank_slot_in_one_snapshot()
    {
        BankerWorld w = await BankerWorld.CreateAsync();
        w.Character.Container(InventoryType.Bank).Load([Item(2, Potion, count: 7)]);

        InventorySlotUpdateDto[] snapshot = BankAccess.Snapshot(w.Character);

        Assert.Equal(30, snapshot.Length);
        Assert.All(snapshot, s => Assert.Equal((ushort)InventoryType.Bank, s.Container));
        Assert.Equal(Enumerable.Range(0, 30).Select(i => (ushort)i), snapshot.Select(s => s.Slot));
        Assert.Equal(7u, snapshot[2].Item!.Count);
        Assert.Equal(29, snapshot.Count(s => s.Item is null));
    }
}
