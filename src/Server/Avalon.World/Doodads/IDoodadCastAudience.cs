using Avalon.World.Public.Units;

namespace Avalon.World.Doodads;

/// <summary>
/// Who hears a doodad's cast bar: MapInstance, which sends the cast packets naming the doodad's template to the user and
/// to the connections near it whose view holds the doodad (CharacterCharacterGameState.Knows), so a player the doodad is
/// hidden from never learns of it through someone else's cast bar.
/// </summary>
public interface IDoodadCastAudience
{
    void BroadcastDoodadCastStart(IUnit caster, Doodad doodad, float castTimeSeconds, uint castId);
    void BroadcastDoodadCastFinish(IUnit caster, Doodad doodad, uint castId);
    void BroadcastDoodadCastInterrupted(IUnit caster, Doodad doodad, uint castId);
}
