using Avalon.Common.ValueObjects;
using Avalon.World.Public.Units;

namespace Avalon.World.Items;

/// <summary>Who hears an item's cast bar (item use): MapInstance, which sends the existing cast packets naming the item.</summary>
public interface IItemCastAudience
{
    void BroadcastItemCastStart(IUnit caster, ItemTemplateId item, float castTimeSeconds, uint castId);
    void BroadcastItemCastFinish(IUnit caster, ItemTemplateId item, uint castId);
    void BroadcastItemCastInterrupted(IUnit caster, ItemTemplateId item, uint castId);
}
