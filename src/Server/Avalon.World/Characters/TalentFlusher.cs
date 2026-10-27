using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Talents;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Quests;
using Avalon.World.Talents;

namespace Avalon.World.Characters;

/// <summary>
/// Sends a character's own client its talent window's state (#1181, SMSG_TALENT_STATE) when it changed: an accepted
/// allocation or a paying turn-in marked it owed, or the level moved since it was last sent. Comparing the level
/// catches every level-up, whatever granted it (anything that grants experience: a kill, a quest, an item), without
/// touching the experience award. Called by WorldServer once per tick per connection, after the character sheet and
/// before the ability amounts, so a tick that changed the state several times sends it once, whole. Select sends the
/// first state itself, since a character reaches its connection only once it is in the world.
/// </summary>
public static class TalentFlusher
{
    /// <summary>
    /// Reads one flag and one level per connection; only a character whose state is owed has its points worked out
    /// (<paramref name="quests" />, read once per tick by the caller, prices its turned-in quests) and is sent anything.
    /// </summary>
    public static void Flush(IWorldConnection connection, QuestCatalog quests)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        TalentAllocation talents = character.Talents;
        ushort level = character.Level;
        if (!talents.StateOwed && talents.StateSentLevel == level)
            return;

        int earned = TalentPoints.Earned(level, character.Quests, quests);
        connection.Send(STalentStatePacket.Create(talents.Held, checked((uint)earned), (uint)talents.Spent,
            PacketEncoder.Shared));
        talents.StateSent(level);
    }
}
