using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One thing an NPC says, with the options it offers. Keyed by creature template, so
/// every Innkeeper says the same thing.</summary>
public class DialogueNode : IDbEntity<DialogueNodeId>
{
    public DialogueNodeId Id { get; set; } = default!;
    public CreatureTemplateId CreatureTemplateId { get; set; } = default!;

    /// <summary>True for the node an interact opens. Exactly one per template that has dialogue.</summary>
    public bool IsRoot { get; set; }

    public LocalizedTextId TextId { get; set; } = default!;
}
