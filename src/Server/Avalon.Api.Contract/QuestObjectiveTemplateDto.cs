namespace Avalon.Api.Contract;

public sealed class QuestObjectiveTemplateDto
{
    public uint Id { get; set; }
    public QuestObjectiveType Type { get; set; }

    /// <summary>The creature a Kill or Talk objective names; null otherwise.</summary>
    public ulong? CreatureTemplateId { get; set; }

    /// <summary>The quest item a Collect objective names; null otherwise.</summary>
    public ulong? ItemTemplateId { get; set; }

    public uint Count { get; set; }
    public string Text { get; set; } = "";
    public int TextId { get; set; }
}
