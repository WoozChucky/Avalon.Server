using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>Opening and closing the bank through the banker's dialogue (spec #463).</summary>
public class BankDialogueShould
{
    private static void Choose(BankerWorld w, int option) =>
        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, w.World).Execute(w.Connection,
            new CDialogueChoosePacket
            {
                TargetGuid = BankerWorld.BankerGuid.RawValue, NodeId = BankerWorld.BankerRoot, OptionId = option,
            });

    private static void Interact(BankerWorld w) =>
        new InteractHandler(NullLogger<InteractHandler>.Instance, w.World).Execute(w.Connection,
            new CInteractPacket { TargetGuid = BankerWorld.BankerGuid.RawValue });

    [Fact]
    public async Task Open_the_bank_and_send_every_bank_slot_once()
    {
        var w = await BankerWorld.CreateAsync();
        w.Character.Container(InventoryType.Bank).Load([Item(2, Potion, count: 7)]);
        Interact(w);

        Choose(w, BankerWorld.OpenBankOption);

        Assert.True(BankAccess.IsOpen(w.Connection, w.Character));
        SInventoryUpdatePacket bank = Assert.Single(w.Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE));
        Assert.Equal(30, bank.Slots.Length);
        Assert.Equal(7u, bank.Slots.Single(s => s.Slot == 2).Item!.Count);
        Assert.Null(bank.Money);
        // The option leads back to the root, so the conversation, and with it the bank, stays open.
        Assert.Equal(2, w.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Count);
        Assert.Empty(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
    }

    [Fact]
    public async Task Close_the_bank_when_the_conversation_ends()
    {
        var w = await BankerWorld.CreateAsync();
        Interact(w);
        Choose(w, BankerWorld.OpenBankOption);

        Choose(w, BankerWorld.FarewellOption);

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.Null(w.Character.OpenBankNpc);
        Assert.Single(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
    }

    [Fact]
    public async Task Close_the_bank_when_the_player_talks_to_the_banker_again()
    {
        var w = await BankerWorld.CreateAsync();
        Interact(w);
        Choose(w, BankerWorld.OpenBankOption);

        Interact(w);

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.NotNull(w.Connection.CurrentDialogue);
        // The client hides the bank window only on SMSG_DIALOGUE_END, so the restart ends the old
        // conversation out loud before it opens the root again.
        SDialogueEndPacket end = Assert.Single(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Equal(BankerWorld.BankerGuid.RawValue, end.SpeakerGuid);
        // The first interact, the choose that led back to the root, and the restart.
        Assert.Equal(3, w.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Count);
    }

    [Fact]
    public async Task Close_the_bank_and_tell_the_client_when_the_player_talks_to_someone_else()
    {
        var w = await BankerWorld.CreateAsync();
        Interact(w);
        Choose(w, BankerWorld.OpenBankOption);

        new InteractHandler(NullLogger<InteractHandler>.Instance, w.World).Execute(w.Connection,
            new CInteractPacket { TargetGuid = BankerWorld.StrangerGuid.RawValue });

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.Null(w.Character.OpenBankNpc);
        SDialogueEndPacket end = Assert.Single(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Equal(BankerWorld.BankerGuid.RawValue, end.SpeakerGuid);
        SDialogueNodePacket last = w.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Last();
        Assert.Equal(BankerWorld.StrangerGuid.RawValue, last.SpeakerGuid);
        Assert.Equal(BankerWorld.StrangerRoot, last.NodeId);
    }

    [Fact]
    public async Task Send_no_bank_slots_for_an_open_bank_option_that_ends_the_conversation()
    {
        var w = await BankerWorld.CreateAsync();
        w.Character.Container(InventoryType.Bank).Load([Item(2, Potion, count: 7)]);
        Interact(w);

        Choose(w, BankerWorld.OpenBankAndLeaveOption);

        Assert.Empty(w.Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE));
        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.Null(w.Character.OpenBankNpc);
        Assert.Single(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
    }

    [Fact]
    public async Task Close_the_bank_when_a_choice_is_made_past_the_leash()
    {
        var w = await BankerWorld.CreateAsync();
        Interact(w);
        Choose(w, BankerWorld.OpenBankOption);
        w.Character.Position = new Avalon.Common.Mathematics.Vector3(0, 0, 30);

        Choose(w, BankerWorld.OpenBankOption);

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.Null(w.Character.OpenBankNpc);
        // Only the first choose sent the bank; the one past the leash sent nothing but the close.
        Assert.Single(w.Read<SInventoryUpdatePacket>(NetworkPacketType.SMSG_INVENTORY_UPDATE));
    }

    private static Dictionary<int, DialogueOptionKind> Kinds(SDialogueNodePacket node) =>
        node.Options.ToDictionary(o => o.OptionId, o => o.Kind);

    /// <summary>
    /// Each option names the action choosing it will run (#522). The OpenBank option that ends the
    /// conversation opens nothing, so it goes out as a plain conversation option.
    /// </summary>
    [Fact]
    public async Task Tell_the_client_which_banker_option_opens_the_bank()
    {
        var w = await BankerWorld.CreateAsync();

        Interact(w);

        SDialogueNodePacket root = Assert.Single(w.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE));
        Assert.Equal(
            new Dictionary<int, DialogueOptionKind>
            {
                [BankerWorld.OpenBankOption] = DialogueOptionKind.OpenBank,
                [BankerWorld.FarewellOption] = DialogueOptionKind.Conversation,
                [BankerWorld.OpenBankAndLeaveOption] = DialogueOptionKind.Conversation,
            },
            Kinds(root));
    }

    [Fact]
    public async Task Send_the_kinds_again_on_the_node_a_choice_leads_to()
    {
        var w = await BankerWorld.CreateAsync();
        Interact(w);

        Choose(w, BankerWorld.OpenBankOption);

        SDialogueNodePacket next = w.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Last();
        Assert.Equal(DialogueOptionKind.OpenBank, Kinds(next)[BankerWorld.OpenBankOption]);
        Assert.Equal(DialogueOptionKind.Conversation, Kinds(next)[BankerWorld.OpenBankAndLeaveOption]);
    }

    [Fact]
    public async Task Send_a_plain_npcs_options_as_conversation()
    {
        var w = await BankerWorld.CreateAsync();

        new InteractHandler(NullLogger<InteractHandler>.Instance, w.World).Execute(w.Connection,
            new CInteractPacket { TargetGuid = BankerWorld.StrangerGuid.RawValue });

        SDialogueNodePacket root = Assert.Single(w.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE));
        Assert.Equal(DialogueOptionKind.Conversation, Assert.Single(root.Options).Kind);
    }
}
