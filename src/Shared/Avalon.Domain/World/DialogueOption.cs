using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>One thing the player can say back.</summary>
public class DialogueOption : IDbEntity<DialogueOptionId>
{
    public DialogueOptionId Id { get; set; } = default!;
    public DialogueNodeId NodeId { get; set; } = default!;
    public LocalizedTextId TextId { get; set; } = default!;

    /// <summary>The node to open next; null ends the conversation.</summary>
    public DialogueNodeId? NextNodeId { get; set; }

    public short SortOrder { get; set; }
}
