using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.World;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Packets;

/// <summary>
/// The quest packets (#433). The client decodes by field number, so each field is pinned alone with its bytes;
/// every pinned value is non-zero. A non-null string is always written, an empty one as a field of length zero,
/// so the string fields' empty bytes appear wherever another field is pinned.
/// </summary>
public class QuestPacketsShould
{
    private const string EmptyTitleAndText = "0a00" + "1200";

    private static string Hex<T>(T message)
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return Convert.ToHexString(stream.ToArray()).ToLowerInvariant();
    }

    [Fact]
    public void Use_the_offer_opcode()
    {
        Assert.Equal(0x30C0, (short)NetworkPacketType.SMSG_QUEST_OFFER);
        Assert.Equal(NetworkPacketType.SMSG_QUEST_OFFER, SQuestOfferPacket.PacketType);
    }

    [Fact]
    public void Keep_the_offer_field_numbers()
    {
        Assert.Equal("0801", Hex(new SQuestOfferPacket { QuestId = 1 }));
        Assert.Equal("1002", Hex(new SQuestOfferPacket { NpcGuid = 2 }));
        Assert.Equal("1802", Hex(new SQuestOfferPacket { Mode = QuestOfferMode.TurnIn }));
        Assert.Equal("2204" + EmptyTitleAndText, Hex(new SQuestOfferPacket { Quest = new QuestDisplayDto() }));
    }

    [Fact]
    public void Keep_the_display_field_numbers()
    {
        Assert.Equal("0a0141" + "1200", Hex(new QuestDisplayDto { Title = "A" }));
        Assert.Equal("0a00" + "120141", Hex(new QuestDisplayDto { Text = "A" }));
        Assert.Equal(EmptyTitleAndText + "1a02" + "1200", Hex(new QuestDisplayDto { Stages = [new QuestStageDto()] }));
        Assert.Equal(EmptyTitleAndText + "2200", Hex(new QuestDisplayDto { Rewards = new QuestRewardsDto() }));
    }

    [Fact]
    public void Keep_the_stage_objective_and_reward_field_numbers()
    {
        Assert.Equal("0801" + "1200", Hex(new QuestStageDto { Sequence = 1 }));
        Assert.Equal("120141", Hex(new QuestStageDto { Text = "A" }));
        Assert.Equal("1200" + "1a02" + "2a00", Hex(new QuestStageDto { Objectives = [new QuestObjectiveDto()] }));

        Assert.Equal("0801" + "2a00", Hex(new QuestObjectiveDto { ObjectiveId = 1 }));
        Assert.Equal("1001" + "2a00", Hex(new QuestObjectiveDto { Kind = QuestObjectiveKind.Kill }));
        Assert.Equal("1803" + "2a00", Hex(new QuestObjectiveDto { TargetId = 3 }));
        Assert.Equal("2004" + "2a00", Hex(new QuestObjectiveDto { Count = 4 }));
        Assert.Equal("2a0141", Hex(new QuestObjectiveDto { Text = "A" }));

        Assert.Equal("0801", Hex(new QuestRewardsDto { Experience = 1 }));
        Assert.Equal("1002", Hex(new QuestRewardsDto { Money = 2 }));
        Assert.Equal("1a02" + "0805", Hex(new QuestRewardsDto { Items = [new QuestRewardItemDto { ItemTemplateId = 5 }] }));
        Assert.Equal("0801", Hex(new QuestRewardItemDto { ItemTemplateId = 1 }));
        Assert.Equal("1002", Hex(new QuestRewardItemDto { Count = 2 }));
    }

    [Fact]
    public void Carry_the_quest_id_on_a_dialogue_option()
    {
        Assert.Equal("1200" + "2005", Hex(new SDialogueOptionInfo { QuestId = 5 }));
    }

    [Fact]
    public void Keep_the_enum_values()
    {
        Assert.Equal(3, (int)DialogueOptionKind.QuestOffer);
        Assert.Equal(4, (int)DialogueOptionKind.QuestTurnIn);
        Assert.Equal(0, (int)QuestObjectiveKind.Unknown);
        Assert.Equal(1, (int)QuestObjectiveKind.Kill);
        Assert.Equal(2, (int)QuestObjectiveKind.Collect);
        Assert.Equal(3, (int)QuestObjectiveKind.Talk);
        Assert.Equal(4, (int)QuestObjectiveKind.Scripted);
        Assert.Equal(0, (int)QuestOfferMode.Unknown);
        Assert.Equal(1, (int)QuestOfferMode.Offer);
        Assert.Equal(2, (int)QuestOfferMode.TurnIn);
        Assert.Equal(0, (int)QuestResult.Unknown);
        Assert.Equal(1, (int)QuestResult.Ok);
        Assert.Equal(10, (int)QuestResult.Error);
    }
}
