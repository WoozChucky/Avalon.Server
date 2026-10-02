using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Characters;

/// <summary>How far one objective of an active quest has got. Deleted with its quest row.</summary>
public class CharacterQuestObjective
{
    public CharacterId CharacterId { get; set; } = default!;
    public uint QuestId { get; set; }
    public uint ObjectiveId { get; set; }
    public uint Progress { get; set; }
}
