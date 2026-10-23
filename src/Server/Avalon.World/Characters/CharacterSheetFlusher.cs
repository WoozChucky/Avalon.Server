using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Reload;

namespace Avalon.World.Characters;

/// <summary>
/// Sends a character's own client its sheet (#506) when it differs from the one last sent. Called by
/// WorldServer once per tick per connection, after both tick passes, so a tick that changed the stats
/// several times (a gear swap and a level-up) or moved a cap (a combat reload) sends one sheet, whole.
/// </summary>
public static class CharacterSheetFlusher
{
    /// <summary>
    /// Only a connection's own character is ever read, so a sheet never reaches another player. A character
    /// is on its connection only once it is in the world (the readiness barrier puts it there), so the first
    /// sheet of a session leaves on the tick it enters. A character with no stats yet is sent nothing. The sheet is
    /// rebuilt only when what it is worked out from moved (<see cref="CharacterEntity.SheetInputs" />): a stats refresh,
    /// the level, the movement speed, or the combat generation, which every <c>/reload combat</c> publishes anew. So most
    /// ticks compare four values and derive nothing.
    /// </summary>
    public static void Flush(IWorldConnection connection, CombatPatch combat)
    {
        // The stats are read (a copy of the whole struct) only once the inputs say the sheet must be rebuilt.
        if (connection.Character is not CharacterEntity character || !character.Stats.HasValue)
            return;

        float movementSpeed = character.GetMovementSpeed();
        (int StatsVersion, ushort Level, CombatPatch? Combat, float MovementSpeed) last = character.SheetInputs;
        if (character.SheetSent is not null
            && ReferenceEquals(last.Combat, combat)
            && last.StatsVersion == character.StatsVersion
            && last.Level == character.Level
            && last.MovementSpeed == movementSpeed)
        {
            return;
        }

        character.SheetInputs = (character.StatsVersion, character.Level, combat, movementSpeed);
        var sheet = CharacterSheet.From(character.Stats.GetValueOrDefault(), character.Level, combat.Formula,
            character.Pace.AttackSpeedPct, movementSpeed);
        if (character.SheetSent == sheet)
            return;

        character.SheetSent = sheet;
        connection.Send(SCharacterStatsPacket.Create(sheet.ToPacket(), PacketEncoder.Shared));
    }
}
