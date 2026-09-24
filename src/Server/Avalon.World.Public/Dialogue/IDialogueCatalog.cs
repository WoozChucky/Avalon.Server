using Avalon.Common.ValueObjects;

namespace Avalon.World.Public.Dialogue;

/// <summary>
/// The dialogue graph, resolved from memory. Loaded once at startup — interact runs on the tick
/// thread, where a database round trip would stall every player on the map.
/// </summary>
public interface IDialogueCatalog
{
    /// <summary>The node an interact opens for this creature, or null when it has no dialogue.</summary>
    DialogueNodeView? GetRoot(CreatureTemplateId creatureTemplateId);

    /// <summary>A node by id, or null when the id is unknown.</summary>
    DialogueNodeView? GetNode(DialogueNodeId nodeId);
}

/// <param name="CreatureTemplateId">
/// Carried so a handler can verify a node belongs to the NPC actually being talked to.
/// </param>
/// <param name="Options">Pre-sorted by the authored sort order.</param>
public sealed record DialogueNodeView(
    DialogueNodeId Id,
    CreatureTemplateId CreatureTemplateId,
    LocalizedTextId TextId,
    IReadOnlyList<DialogueOptionView> Options);

/// <param name="NextNodeId">The node to open next; null ends the conversation.</param>
public sealed record DialogueOptionView(
    DialogueOptionId Id,
    LocalizedTextId TextId,
    DialogueNodeId? NextNodeId);
