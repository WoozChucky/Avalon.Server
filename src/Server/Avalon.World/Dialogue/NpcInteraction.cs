using Avalon.Common.Mathematics;
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

    /// <summary>
    /// Metres. How far a player may drift from the NPC while a conversation with it stays open.
    /// Deliberately three times <c>InteractHandler</c>'s 5 m interact range: opening a conversation
    /// needs you standing next to the NPC, keeping it open only needs you to still be with them.
    /// </summary>
    public const float LeashRange = 15f;

    /// <summary>
    /// The dialogue leash: true while the character is close enough to the NPC for an open
    /// conversation with it to stay open. Inclusive, so exactly <see cref="LeashRange"/> is inside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every handler that acts on an open NPC conversation must pass this check before it acts,</b>
    /// and end the conversation (clear <c>CurrentDialogue</c>, send <c>SMSG_DIALOGUE_END</c>) when it
    /// fails. <c>DialogueChooseHandler</c> does, on every choose. A vendor, quest, payment or item
    /// effect reached through a conversation (#432) must too; otherwise opening a conversation once
    /// next to the NPC lets the player trigger that effect from anywhere on the map, because
    /// <c>CurrentDialogue</c> lives until the connection leaves the instance.
    /// </para>
    /// <para>
    /// It is not the 5 m interact check on purpose: stepping back a metre mid-sentence must not slam
    /// the window shut. The test is <c>distance &lt;= LeashRange</c> and callers reject on its
    /// negation, <c>!(distance &lt;= LeashRange)</c>, as <c>LootPickup</c> does: a NaN distance
    /// compares false against everything, so it lands out of range. A <c>distance &gt; LeashRange</c>
    /// rejection would let it through.
    /// </para>
    /// </remarks>
    public static bool IsWithinLeash(Vector3 characterPosition, Vector3 npcPosition)
    {
        float distance = Vector3.Distance(characterPosition, npcPosition);
        return distance <= LeashRange;
    }
}
