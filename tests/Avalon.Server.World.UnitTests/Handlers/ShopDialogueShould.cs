using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Vendor;
using Avalon.Network.Packets.World;
using Avalon.Server.World.UnitTests.Vendors;
using Avalon.World.Vendors;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>Opening and closing a shop through a vendor's dialogue (spec #432), mirroring BankDialogueShould.</summary>
public class ShopDialogueShould : IAsyncLifetime
{
    private VendorWorld _w = null!;

    public async Task InitializeAsync() => _w = await VendorWorld.CreateAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private bool ShopOpen => ShopAccess.IsOpen(_w.Main.Connection, _w.Main.Character);

    [Fact]
    public void Open_the_shop_and_send_the_list_once()
    {
        _w.OpenShop();

        Assert.True(ShopOpen);
        SVendorListPacket list = Assert.Single(_w.Main.Lists());
        Assert.Equal(VendorWorld.SmithGuid.RawValue, list.VendorGuid);
        // Row 5 is behind a quest nobody can have yet.
        Assert.Equal([1u, 2u, 3u, 4u], list.Entries.Select(e => e.Sequence));
        Assert.Empty(list.Buyback);   // nothing sold yet: absent on the wire, read back as empty
        // The option leads back to the root, so the conversation, and the shop with it, stays open.
        Assert.Equal(2, _w.Main.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Count);
        Assert.Empty(_w.Main.Ends());
    }

    [Fact]
    public void Close_the_shop_when_the_conversation_ends_with_farewell()
    {
        _w.OpenShop();

        _w.Choose(_w.Main, VendorWorld.SmithGuid, VendorWorld.SmithRoot, VendorWorld.SmithFarewell);

        Assert.False(ShopOpen);
        Assert.Null(_w.Main.Character.OpenShopNpc);
        Assert.Single(_w.Main.Ends());
    }

    [Fact]
    public void Close_the_shop_out_loud_when_the_player_talks_to_the_vendor_again()
    {
        _w.OpenShop();

        _w.Interact(_w.Main, VendorWorld.SmithGuid);

        Assert.False(ShopOpen);
        Assert.Equal(VendorWorld.SmithGuid.RawValue, Assert.Single(_w.Main.Ends()).SpeakerGuid);
        Assert.Equal(VendorWorld.SmithGuid, _w.Main.Connection.CurrentDialogue!.Value.Npc);   // restarted at the root
    }

    [Fact]
    public void Close_the_shop_when_the_player_talks_to_another_vendor()
    {
        _w.OpenShop();

        _w.Interact(_w.Main, VendorWorld.PedlarGuid);

        Assert.False(ShopOpen);
        Assert.Null(_w.Main.Character.OpenShopNpc);
        Assert.Equal(VendorWorld.SmithGuid.RawValue, Assert.Single(_w.Main.Ends()).SpeakerGuid);
    }

    [Fact]
    public void Send_no_list_for_a_shop_option_that_ends_the_conversation()
    {
        _w.Interact(_w.Main, VendorWorld.SmithGuid);

        _w.Choose(_w.Main, VendorWorld.SmithGuid, VendorWorld.SmithRoot, VendorWorld.SmithWaresAndLeave);

        Assert.Empty(_w.Main.Lists());
        Assert.Null(_w.Main.Character.OpenShopNpc);
        Assert.False(_w.Stocks.TryGet(VendorWorld.SmithGuid, out _));
    }

    [Fact]
    public void Close_the_shop_when_a_choice_is_made_past_the_leash()
    {
        _w.OpenShop();
        _w.Main.Character.Position = new Vector3(0, 0, 25);

        _w.Choose(_w.Main, VendorWorld.SmithGuid, VendorWorld.SmithRoot, VendorWorld.SmithWares);

        Assert.False(ShopOpen);
        Assert.Single(_w.Main.Lists());   // the first opening only
        Assert.Single(_w.Main.Ends());
    }

    /// <summary>The conversation goes on, but no shop opens where no stock can be kept.</summary>
    [Fact]
    public void Open_no_shop_in_an_instance_that_keeps_no_stock()
    {
        _w.Main.Character.InstanceId = VendorWorld.PlainInstanceId;

        _w.OpenShop();

        Assert.Null(_w.Main.Character.OpenShopNpc);
        Assert.Empty(_w.Main.Lists());
        Assert.Equal(2, _w.Main.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Count);
    }

    private Dictionary<int, DialogueOptionKind> LastNodeKinds() =>
        _w.Main.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Last()
            .Options.ToDictionary(o => o.OptionId, o => o.Kind);

    /// <summary>
    /// Each option names the action choosing it will run (#522), so a client without a shop can
    /// hide it. The shop option that ends the conversation opens nothing and goes out as a plain one.
    /// </summary>
    [Fact]
    public void Tell_the_client_which_vendor_option_opens_the_shop()
    {
        _w.Interact(_w.Main, VendorWorld.SmithGuid);

        Assert.Equal(
            new Dictionary<int, DialogueOptionKind>
            {
                [VendorWorld.SmithWares] = DialogueOptionKind.OpenShop,
                [VendorWorld.SmithFarewell] = DialogueOptionKind.Conversation,
                [VendorWorld.SmithWaresAndLeave] = DialogueOptionKind.Conversation,
            },
            LastNodeKinds());
    }

    [Fact]
    public void Send_the_kinds_again_on_the_node_the_shop_option_leads_to()
    {
        _w.OpenShop();

        Assert.Equal(2, _w.Main.Read<SDialogueNodePacket>(NetworkPacketType.SMSG_DIALOGUE_NODE).Count);
        Assert.Equal(DialogueOptionKind.OpenShop, LastNodeKinds()[VendorWorld.SmithWares]);
        Assert.Equal(DialogueOptionKind.Conversation, LastNodeKinds()[VendorWorld.SmithFarewell]);
    }

    [Fact]
    public void Send_a_plain_npcs_options_as_conversation()
    {
        _w.Interact(_w.Main, VendorWorld.InnkeeperGuid);

        Assert.Equal(DialogueOptionKind.Conversation, Assert.Single(LastNodeKinds()).Value);
    }
}
