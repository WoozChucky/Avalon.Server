using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// <see cref="IHitQuery" /> over an instance's live dictionaries. Tick thread only, like the
/// dictionaries it reads.
/// </summary>
public sealed class UnitHitQuery(
    IReadOnlyDictionary<ObjectGuid, ICharacter> characters,
    IReadOnlyDictionary<ObjectGuid, ICreature> creatures) : IHitQuery
{
    public IReadOnlyList<IUnit> InCircle(Vector3 centre, float radius) =>
        Collect(centre, unit => HitShapes.CircleOverlaps(centre, radius, unit.Position, unit.BodyRadius));

    public IReadOnlyList<IUnit> InCone(Vector3 origin, Vector3 direction, float reach, float arcDegrees) =>
        Collect(origin, unit => HitShapes.ConeOverlaps(origin, direction, reach, arcDegrees, unit.Position, unit.BodyRadius));

    public IReadOnlyList<IUnit> OnSegment(Vector3 from, Vector3 to) =>
        Collect(from, unit => HitShapes.SegmentOverlaps(from, to, unit.Position, unit.BodyRadius));

    /// <summary>
    /// A character is dead while <c>IsDead</c>. A creature has no dead flag: it is dead at 0 health,
    /// and its corpse stays in the instance until the corpse remover takes it.
    /// </summary>
    public static bool IsAlive(IUnit unit) =>
        unit is ICharacter character ? !character.IsDead : unit.CurrentHealth > 0;

    private List<IUnit> Collect(Vector3 origin, Func<IUnit, bool> overlaps)
    {
        List<IUnit> hits = [];

        foreach (ICharacter character in characters.Values)
        {
            if (IsAlive(character) && overlaps(character))
                hits.Add(character);
        }

        foreach (ICreature creature in creatures.Values)
        {
            if (IsAlive(creature) && overlaps(creature))
                hits.Add(creature);
        }

        hits.Sort((a, b) =>
        {
            int byDistance = HitShapes.Distance2D(origin, a.Position).CompareTo(HitShapes.Distance2D(origin, b.Position));
            return byDistance != 0 ? byDistance : a.Guid.RawValue.CompareTo(b.Guid.RawValue);
        });

        return hits;
    }
}
