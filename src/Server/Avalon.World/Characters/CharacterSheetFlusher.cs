using Avalon.Domain.World;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public;

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
    /// sheet of a session leaves on the tick it enters. A character with no stats yet is sent nothing.
    /// </summary>
    public static void Flush(IWorldConnection connection, CombatFormula formula)
    {
        if (connection.Character is not CharacterEntity { Stats: { } stats } character)
            return;

        CharacterSheet sheet = CharacterSheet.From(stats, formula, character.EffectiveHastePct, character.GetMovementSpeed());
        if (character.SheetSent == sheet)
            return;

        character.SheetSent = sheet;
        connection.Send(SCharacterStatsPacket.Create(sheet.ToPacket(), connection.CryptoSession.Encrypt));
    }
}
