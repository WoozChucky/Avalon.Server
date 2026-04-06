using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Instances;

/// <summary>
/// The simulation context that AI scripts and the spell system interact with at runtime.
/// Implemented by <c>MapInstance</c>; replaces <c>IChunk</c> as the script-facing contract.
/// </summary>
public interface ISimulationContext
{
    IReadOnlyDictionary<ObjectGuid, ICharacter> Characters { get; }
    IReadOnlyDictionary<ObjectGuid, ICreature> Creatures { get; }

    /// <summary>Returns the navigator whose bounds contain <paramref name="position"/>.</summary>
    IChunkNavigator GetNavigatorForPosition(Vector3 position);

    bool QueueSpell(ICharacter caster, IUnit? target, ISpell spell);
    void RespawnCreature(ICreature creature);
    void RemoveCreature(ICreature creature);
    void BroadcastUnitHit(IUnit attacker, IUnit target, uint currentHealth, uint damage);
    void BroadcastUnitStartCast(IUnit caster, float castTime);
}
