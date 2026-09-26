using Avalon.Common.ValueObjects;
using Avalon.World.Public.Dialogue;

namespace Avalon.World.Dialogue;

/// <summary>
/// The one rule for whether interacting with a creature does anything. <c>InteractHandler</c>
/// decides with it whether to open a conversation, and <c>CreatureSpawner</c> decides with it
/// what <c>ObjectState.CanInteract</c> tells a client, so the prompt a client offers and the
/// interact the server accepts cannot drift apart.
/// </summary>
/// <remarks>
/// Today an interaction is a conversation, so the rule is "the template has a dialogue root".
/// When vendors arrive they join it here, and both callers follow.
/// </remarks>
public static class NpcInteraction
{
    /// <summary>The node an interact opens for this template, or null when it has no dialogue.</summary>
    public static DialogueNodeView? RootFor(IDialogueCatalog dialogue, CreatureTemplateId templateId)
        => dialogue.GetRoot(templateId);

    /// <summary>True when interacting with a creature of this template will do something.</summary>
    public static bool CanInteract(IDialogueCatalog dialogue, CreatureTemplateId templateId)
        => RootFor(dialogue, templateId) is not null;
}
