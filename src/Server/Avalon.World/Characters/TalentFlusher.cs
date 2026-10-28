using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Talents;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Quests;
using Avalon.World.Talents;

namespace Avalon.World.Characters;

/// <summary>
/// Sends a character's own client what a talent change owes it (#1181): the ability list (SMSG_CHARACTER_ABILITIES)
/// when a change of the held nodes changed the abilities held, and the talent window's state (SMSG_TALENT_STATE) when it
/// changed: an accepted allocation or a paying turn-in marked it owed, or the level moved since it was last sent.
/// Comparing the level catches every level-up, whatever granted it (anything that grants experience: a kill, a quest, an
/// item), without touching the experience award. Called by WorldServer once per tick per connection, after the character
/// sheet and before the ability amounts, so a tick that changed what is held several times sends one list and one state,
/// whole, after every request's result (Part C: an allocate and refund loop costs one list per tick). The list records
/// the amounts it carries, so <see cref="AbilityAmountsFlusher" />, next, sends nothing on top. Select sends the first
/// list and state itself, since a character reaches its connection only once it is in the world.
/// </summary>
public static class TalentFlusher
{
    /// <summary>
    /// Reads two flags and one level per connection; only a character that is owed something is sent anything, and only
    /// one whose state is owed has its points worked out (<paramref name="quests" />, read once per tick by the caller,
    /// prices its turned-in quests). The list goes first, so the state finds the client holding what it shows.
    /// </summary>
    public static void Flush(IWorldConnection connection, QuestCatalog quests)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        TalentAllocation talents = character.Talents;
        ushort level = character.Level;
        bool stateOwed = talents.StateOwed || talents.StateSentLevel != level;
        if (!stateOwed && !talents.AbilitiesOwed)
            return;

        if (talents.AbilitiesOwed)
        {
            CharacterAbilitiesSender.Send(connection, character);
            talents.AbilitiesSent();
        }

        if (!stateOwed)
            return;

        int earned = TalentPoints.Earned(level, character.Quests, quests);
        connection.Send(STalentStatePacket.Create(talents.Held, checked((uint)earned), (uint)talents.Spent,
            PacketEncoder.Shared));
        talents.StateSent(level);
    }
}
