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

/// <summary>Where a held quest stands (#433). Append-only.</summary>
public enum QuestStateKind
{
    Unknown = 0,
    Active = 1,
    ReadyToTurnIn = 2,
}

/// <summary>What an SMSG_QUEST_UPDATE says happened (#433). Append-only.</summary>
public enum QuestUpdateKind
{
    Unknown = 0,
    /// <summary>Newly accepted: the update carries the display data, as the log does.</summary>
    Accepted = 1,
    /// <summary>A count, the stage or the state changed: progress only, no display data.</summary>
    Progress = 2,
    /// <summary>Abandoned: drop it from the log.</summary>
    Removed = 3,
    /// <summary>Turned in: drop it from the log and count it completed.</summary>
    Completed = 4,
}

/// <summary>What a quest NPC shows over its head for one character (#433). Append-only.</summary>
public enum QuestMarker
{
    Unknown = 0,
    None = 1,
    Available = 2,
    ReadyToTurnIn = 3,
}

[ProtoContract]
public class QuestProgressDto
{
    [ProtoMember(1)] public uint ObjectiveId { get; set; }
    [ProtoMember(2)] public uint Progress { get; set; }
}

[ProtoContract]
public class QuestLogEntryDto
{
    [ProtoMember(1)] public uint QuestId { get; set; }
    [ProtoMember(2)] public QuestStateKind State { get; set; }
    [ProtoMember(3)] public int Stage { get; set; }
    [ProtoMember(4)] public QuestDisplayDto? Display { get; set; }

    /// <summary>Every objective of the quest, any stage, with its count (0 for one not reached).</summary>
    [ProtoMember(5)] public List<QuestProgressDto> Progress { get; set; } = [];
}

[ProtoContract]
public class QuestMarkerDto
{
    /// <summary>Raw ObjectGuid of the NPC creature.</summary>
    [ProtoMember(1)] public ulong CreatureGuid { get; set; }
    [ProtoMember(2)] public QuestMarker Marker { get; set; }
}
