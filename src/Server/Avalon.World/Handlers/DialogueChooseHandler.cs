using Avalon.Common;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.World.Dialogue;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Localization;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Advances a conversation. Every rejection here means a client sent something it was not offered,
/// so they log at information rather than debug — worth seeing.
/// </summary>
/// <remarks>
/// Range is re-checked on every choose, but against the generous dialogue leash
/// (<see cref="NpcInteraction.LeashRange"/>, 15 m), not the 5 m interact range that opening a
/// conversation needs. Stepping back a metre mid-sentence should not slam a window shut, so the
/// 5 m check would be wrong here. No check at all would be wrong too: <c>CurrentDialogue</c> lives
/// until the connection leaves the instance, and once an option does something (a vendor, a quest,
/// a payment, #432) an unchecked choose would let a player do it from anywhere on the map after
/// opening the conversation once next to the NPC. Past the leash the conversation ends with
/// <c>SMSG_DIALOGUE_END</c>, like the other close paths, and nothing advances. Any other handler
/// acting on an open conversation must pass <see cref="NpcInteraction.IsWithinLeash"/> as well.
/// The NPC still existing and being alive is re-checked too, because talking to a corpse is
/// nonsense the player can see.
/// </remarks>
[PacketHandler(NetworkPacketType.CMSG_DIALOGUE_CHOOSE)]
public class DialogueChooseHandler(ILogger<DialogueChooseHandler> logger, IWorld world)
    : WorldPacketHandler<CDialogueChoosePacket>
{
    private IDialogueCatalog Dialogue => world.Data.Dialogue;
    private ILocalizedTextCatalog Text => world.Data.LocalizedTexts;

    public override void Execute(IWorldConnection connection, CDialogueChoosePacket packet)
    {
        ICharacter? character = connection.Character;
        if (character is null || character.IsDead)
        {
            logger.LogDebug("Dropped CMSG_DIALOGUE_CHOOSE from dead/missing char");
            return;
        }

        if (connection.CurrentDialogue is not { } open)
        {
            logger.LogInformation("Dialogue choose with no conversation open");
            return;
        }

        if (open.Npc.RawValue != packet.TargetGuid)
        {
            logger.LogInformation("Dialogue choose for {Sent} while talking to {Open}",
                packet.TargetGuid, open.Npc.RawValue);
            return;
        }

        if (open.Node.Value != packet.NodeId)
        {
            logger.LogInformation("Dialogue choose against node {Sent} while showing {Open}",
                packet.NodeId, open.Node.Value);
            return;
        }

        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(character.InstanceId);
        if (context is null)
        {
            logger.LogWarning("Instance not found for character {CharacterId}", character.Guid);
            return;
        }

        // The NPC may have died or been removed since the last node was sent.
        if (!context.Creatures.TryGetValue(open.Npc, out ICreature? npc) || npc.CurrentHealth == 0)
        {
            logger.LogInformation("Dialogue partner {Npc} is gone; closing the conversation", open.Npc);
            End(connection, open.Npc);
            return;
        }

        // The leash, after the NPC check because it needs the NPC's position. Walking away closes
        // the conversation rather than just dropping the choice, so the client's window cannot
        // stay open on a conversation the server no longer honours.
        if (!NpcInteraction.IsWithinLeash(character.Position, npc.Position))
        {
            logger.LogInformation("Dialogue partner {Npc} is past the {Leash} m leash; closing the conversation",
                open.Npc, NpcInteraction.LeashRange);
            End(connection, open.Npc);
            return;
        }

        DialogueNodeView? current = Dialogue.GetNode(open.Node);
        if (current is null || current.CreatureTemplateId != npc.Metadata.Id)
        {
            logger.LogWarning("Open dialogue node {Node} does not belong to creature {TemplateId}",
                open.Node.Value, npc.Metadata.Id);
            End(connection, open.Npc);
            return;
        }

        DialogueOptionView? chosen = current.Options.FirstOrDefault(o => o.Id.Value == packet.OptionId);
        if (chosen is null)
        {
            logger.LogInformation("Dialogue option {Option} is not offered by node {Node}",
                packet.OptionId, open.Node.Value);
            return;
        }

        if (chosen.NextNodeId is not { } nextId)
        {
            End(connection, open.Npc);
            return;
        }

        DialogueNodeView? next = Dialogue.GetNode(nextId);
        if (next is null)
        {
            // Broken content closes the window rather than wedging it open.
            logger.LogWarning("Dialogue option {Option} leads to unknown node {Node}",
                chosen.Id.Value, nextId.Value);
            End(connection, open.Npc);
            return;
        }

        connection.CurrentDialogue = (open.Npc, next.Id);
        InteractHandler.Send(connection, npc, next, character, Text);
    }

    private static void End(IWorldConnection connection, ObjectGuid npc)
    {
        connection.CurrentDialogue = null;
        connection.Send(SDialogueEndPacket.Create(npc.RawValue, connection.CryptoSession.Encrypt));
    }
}
