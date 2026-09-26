using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.World;
using Avalon.World.Dialogue;
using Avalon.World.Entities;
using Avalon.World.Inventory;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Localization;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

/// <summary>
/// Opens a conversation with an NPC. Every rejection is a silent drop: a client that asks to talk
/// to a monster, a corpse, or something across the map gets no reply rather than an error packet.
/// </summary>
[PacketHandler(NetworkPacketType.CMSG_INTERACT)]
public class InteractHandler(ILogger<InteractHandler> logger, IWorld world)
    : WorldPacketHandler<CInteractPacket>
{
    // The catalogs live on StaticData, not in DI — they are built during World.LoadAsync once the
    // reference data has been read. Every other handler reaches static data the same way
    // (CharacterCreateHandler's world.Data.ClassLevelStats, CharacterSelectHandler's
    // world.Data.CharacterLevelExperiences), so follow that rather than registering a holder.
    private IDialogueCatalog Dialogue => world.Data.Dialogue;
    private ILocalizedTextCatalog Text => world.Data.LocalizedTexts;

    /// <summary>
    /// Metres. A local constant, matching CastAbilityHandler's MaxFacingAngle. There is no facing
    /// check: you do not have to be looking at someone to talk to them, and the 65 degree cone
    /// exists for combat.
    /// </summary>
    private const float InteractRange = 5f;

    public override void Execute(IWorldConnection connection, CInteractPacket packet)
    {
        ICharacter? character = connection.Character;
        if (character is null || character.IsDead)
        {
            logger.LogDebug("Dropped CMSG_INTERACT from dead/missing char");
            return;
        }

        ISimulationContext? context = world.InstanceRegistry.GetInstanceById(character.InstanceId);
        if (context is null)
        {
            logger.LogWarning("Instance not found for character {CharacterId}", character.Guid);
            return;
        }

        var targetGuid = new ObjectGuid(packet.TargetGuid);
        if (!context.Creatures.TryGetValue(targetGuid, out ICreature? npc))
        {
            logger.LogDebug("Interact reject TargetNotFound target={TargetGuid}", packet.TargetGuid);
            return;
        }

        if (npc.CurrentHealth == 0)
        {
            logger.LogDebug("Interact reject TargetDead target={TargetGuid}", packet.TargetGuid);
            return;
        }

        // The same rule CreatureSpawner used to set ObjectState.CanInteract, so a creature the
        // client offered a prompt for is one this accepts. The catalog stays the authority: a
        // creature spawned before a /reload dialogue still carries its old flag, and this answers
        // from the current catalog either way.
        DialogueNodeView? root = NpcInteraction.RootFor(Dialogue, npc.Metadata.Id);
        if (root is null)
        {
            // Every monster in the game lands here, so this is not worth a warning.
            logger.LogTrace("Interact on creature {TemplateId} which has no dialogue", npc.Metadata.Id);
            return;
        }

        float distance = Vector3.Distance(character.Position, npc.Position);
        if (distance > InteractRange)
        {
            logger.LogDebug("Interact reject Range dist={Distance} max={Max}", distance, InteractRange);
            return;
        }

        // A new conversation, even with the same banker, starts with the bank closed. The old one is
        // ended out loud when it was with someone else, or when it had the bank open: the client
        // hides the bank window only on SMSG_DIALOGUE_END (#463).
        bool bankOpen = character is CharacterEntity entity && BankAccess.IsOpen(connection, entity);
        if (connection.CurrentDialogue is { } old && (old.Npc != npc.Guid || bankOpen))
            NpcInteraction.EndConversation(connection, old.Npc);
        else if (character is CharacterEntity stale)
            stale.OpenBankNpc = null;

        // Interacting again mid-conversation restarts at the root, which is what clicking an NPC
        // twice should do and what unwedges a client that lost its window.
        connection.CurrentDialogue = (npc.Guid, root.Id);
        SendNode(connection, npc, root, character);
    }

    /// <summary>
    /// Shared with DialogueChooseHandler's advance path. One TextContext is built and reused across
    /// the node and all its options — the class-name resolution inside it is the expensive part.
    /// </summary>
    internal static void Send(
        IWorldConnection connection,
        ICreature npc,
        DialogueNodeView node,
        ICharacter character,
        ILocalizedTextCatalog text)
    {
        TextContext context = text.ContextFor(character, connection.Locale);

        List<SDialogueOptionInfo> options = node.Options
            .Select(option => new SDialogueOptionInfo
            {
                OptionId = option.Id.Value,
                Text = text.Get(option.TextId, context)
            })
            .ToList();

        connection.Send(SDialogueNodePacket.Create(
            npc.Guid.RawValue,
            npc.Name,
            node.Id.Value,
            text.Get(node.TextId, context),
            options,
            connection.CryptoSession.Encrypt));
    }

    private void SendNode(IWorldConnection connection, ICreature npc, DialogueNodeView node, ICharacter character)
        => Send(connection, npc, node, character, Text);
}
