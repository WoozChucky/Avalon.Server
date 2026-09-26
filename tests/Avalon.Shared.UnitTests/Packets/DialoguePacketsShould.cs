using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

public class DialoguePacketsShould
{
    private static byte[] Plain(ReadOnlySpan<byte> bytes) => bytes.ToArray();

    [Fact]
    public void Carry_Each_Options_Kind_Through_A_Round_Trip()
    {
        NetworkPacket packet = SDialogueNodePacket.Create(7, "Marta", 1, "Coin and keepsakes.",
            [
                new SDialogueOptionInfo { OptionId = 1, Text = "Open my bank.", Kind = DialogueOptionKind.OpenBank },
                new SDialogueOptionInfo { OptionId = 2, Text = "Show me your wares.", Kind = DialogueOptionKind.OpenShop },
                new SDialogueOptionInfo { OptionId = 3, Text = "Farewell.", Kind = DialogueOptionKind.Conversation },
            ],
            Plain);

        using var stream = new MemoryStream(packet.Payload);
        SDialogueNodePacket read = Serializer.Deserialize<SDialogueNodePacket>(stream);

        Assert.Equal(
            [DialogueOptionKind.OpenBank, DialogueOptionKind.OpenShop, DialogueOptionKind.Conversation],
            read.Options.ConvertAll(o => o.Kind));
    }

    [Fact]
    public void Read_An_Option_Without_A_Kind_As_Conversation()
    {
        // Field 1 = 9, field 2 = "Hi": what an option looked like before field 3 existed.
        byte[] payload = [0x08, 0x09, 0x12, 0x02, (byte)'H', (byte)'i'];

        using var stream = new MemoryStream(payload);
        SDialogueOptionInfo read = Serializer.Deserialize<SDialogueOptionInfo>(stream);

        Assert.Equal(9, read.OptionId);
        Assert.Equal("Hi", read.Text);
        Assert.Equal(DialogueOptionKind.Conversation, read.Kind);
    }

    /// <summary>A client decodes these numbers, so the enum is append-only (#522).</summary>
    [Fact]
    public void Keep_The_Wire_Numbers()
    {
        Assert.Equal(0, (int)DialogueOptionKind.Conversation);
        Assert.Equal(1, (int)DialogueOptionKind.OpenBank);
        Assert.Equal(2, (int)DialogueOptionKind.OpenShop);
    }
}
