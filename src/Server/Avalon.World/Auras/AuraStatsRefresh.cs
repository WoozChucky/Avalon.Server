using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Public.Units;

namespace Avalon.World.Auras;

/// <summary>
/// After an aura with stat modifiers is applied, stacked or removed: a character's stats are refreshed, each pool
/// keeping its share (KeepShare), so a buff's end never kills; a creature folds its auras on the spot. Tick thread.
/// </summary>
public static class AuraStatsRefresh
{
    /// <param name="data">The current reference data; without it (tests) a character's stats are left as they are.</param>
    public static void Apply(IUnit unit, StaticData? data)
    {
        switch (unit)
        {
            case CharacterEntity character when data is not null:
                CharacterStatsRefresh.Apply(character, data, CurrentValues.KeepShare);
                break;
            case Creature creature:
                creature.ApplyAuraStats(creature.Auras.StatTotals);
                break;
        }
    }
}
