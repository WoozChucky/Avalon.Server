using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Talents;
using Avalon.World.Configuration;
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
    /// prices its turned-in quests; <paramref name="config" />, Game:Talents, prices a refund at the character's level). The
    /// list goes first, so the state finds the client holding what it shows.
    /// </summary>
    public static void Flush(IWorldConnection connection, QuestCatalog quests, TalentConfiguration config)
    {
        if (connection.Character is not CharacterEntity character)
            return;

        TalentAllocation talents = character.Talents;
        ushort level = character.Level;
        bool stateOwed = talents.StateOwed || talents.StateSentLevel != level;
        if (!stateOwed && !talents.AbilitiesOwed)
            return;

        // Each flag is cleared before its send, as the sheet records what it sends first: a send that throws is not tried
        // again on every tick, stalling the flush of every connection after it each time.
        if (talents.AbilitiesOwed)
        {
            talents.AbilitiesSent();
            CharacterAbilitiesSender.Send(connection, character);
        }

        if (!stateOwed)
            return;

        talents.StateSent(level);
        int earned = TalentPoints.Earned(level, character.Quests, quests);
        connection.Send(STalentStatePacket.Create(talents.Held, checked((uint)earned), (uint)talents.Spent,
            TalentPrice.PerNode(level, config), PacketEncoder.Shared));
    }
}
