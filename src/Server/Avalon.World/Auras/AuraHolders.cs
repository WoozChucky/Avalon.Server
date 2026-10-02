using Avalon.World.Entities;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>Who can hold auras: the World-side character and creature. Any other unit holds none, which fails safe.</summary>
public static class AuraHolders
{
    public static UnitAuras? Of(IUnit unit) => unit switch
    {
        CharacterEntity character => character.Auras,
        Creature creature => creature.Auras,
        _ => null,
    };
}
