using Avalon.Common.ValueObjects;
using Avalon.World.Public.Units;

namespace Avalon.World.Doodads;

/// <summary>
/// Who hears a doodad's cast bar: MapInstance, which sends the cast packets naming the doodad's template to every
/// connection near the caster, as for an item's.
/// </summary>
public interface IDoodadCastAudience
{
    void BroadcastDoodadCastStart(IUnit caster, DoodadTemplateId doodad, float castTimeSeconds, uint castId);
    void BroadcastDoodadCastFinish(IUnit caster, DoodadTemplateId doodad, uint castId);
    void BroadcastDoodadCastInterrupted(IUnit caster, DoodadTemplateId doodad, uint castId);
}
