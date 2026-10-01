using ProtoBuf;

namespace Avalon.Network.Packets.Quest;

/// <summary>What an objective counts (#433). Append-only.</summary>
public enum QuestObjectiveKind
{
    Unknown = 0,
    Kill = 1,
    Collect = 2,
    Talk = 3,
    Scripted = 4,
}

/// <summary>Why SMSG_QUEST_OFFER was sent (#433). Append-only.</summary>
public enum QuestOfferMode
{
    Unknown = 0,
    /// <summary>The NPC offers the quest; the client may send CMSG_QUEST_ACCEPT.</summary>
    Offer = 1,
    /// <summary>The quest is ready; the client may send CMSG_QUEST_TURN_IN. The text is the completion text.</summary>
    TurnIn = 2,
}

/// <summary>One objective, as the client shows it. TargetId is a creature template (Kill, Talk) or item template (Collect); 0 for Scripted.</summary>
[ProtoContract]
public class QuestObjectiveDto
{
    [ProtoMember(1)] public uint ObjectiveId { get; set; }
    [ProtoMember(2)] public QuestObjectiveKind Kind { get; set; }
    [ProtoMember(3)] public ulong TargetId { get; set; }
    [ProtoMember(4)] public uint Count { get; set; }
    [ProtoMember(5)] public string Text { get; set; } = string.Empty;
}

[ProtoContract]
public class QuestStageDto
{
    [ProtoMember(1)] public int Sequence { get; set; }
    [ProtoMember(2)] public string Text { get; set; } = string.Empty;
    [ProtoMember(3)] public List<QuestObjectiveDto> Objectives { get; set; } = [];
}

[ProtoContract]
public class QuestRewardItemDto
{
    [ProtoMember(1)] public ulong ItemTemplateId { get; set; }
    [ProtoMember(2)] public uint Count { get; set; }
}

/// <summary>Money is copper.</summary>
[ProtoContract]
public class QuestRewardsDto
{
    [ProtoMember(1)] public uint Experience { get; set; }
    [ProtoMember(2)] public ulong Money { get; set; }
    [ProtoMember(3)] public List<QuestRewardItemDto> Items { get; set; } = [];
}

/// <summary>
/// Everything a client needs to show a quest, text already resolved in the character's language (#433): the
/// packets are self-describing, so the client needs no quest catalog.
/// </summary>
[ProtoContract]
public class QuestDisplayDto
{
    [ProtoMember(1)] public string Title { get; set; } = string.Empty;

    /// <summary>The description, or in a TurnIn offer the completion text.</summary>
    [ProtoMember(2)] public string Text { get; set; } = string.Empty;

    [ProtoMember(3)] public List<QuestStageDto> Stages { get; set; } = [];
    [ProtoMember(4)] public QuestRewardsDto? Rewards { get; set; }
}
