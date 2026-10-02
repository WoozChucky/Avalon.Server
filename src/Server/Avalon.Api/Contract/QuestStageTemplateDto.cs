namespace Avalon.Api.Contract;

public sealed class QuestStageTemplateDto
{
    public int Sequence { get; set; }

    /// <summary>The stage's line, or null when it has none.</summary>
    public string? Text { get; set; }
    public int? TextId { get; set; }

    /// <summary>By objective id.</summary>
    public IList<QuestObjectiveTemplateDto> Objectives { get; set; } = [];
}
