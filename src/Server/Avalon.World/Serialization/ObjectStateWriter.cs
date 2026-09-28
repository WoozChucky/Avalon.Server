using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

namespace Avalon.World.Serialization;

/// <summary>
/// Describes an entity as an <see cref="ObjectState" /> for replication.
/// </summary>
/// <remarks>
/// <see cref="GameEntityFields" /> still decides what goes out. It is the server's own record
/// of what has changed and it keeps that job; what it has stopped being is the wire format,
/// where it used to head the payload as a bitmask a reader had to decode before it knew how
/// many bytes followed. A field it selects now becomes a member that is present, and one it
/// does not select is simply not there.
///
/// Which overload applies is decided by the static type of the entity, exactly as the call
/// sites already decide it, so an entity kind gets the members its kind has.
///
/// Each overload takes an optional <see cref="ObjectStatePool" />. With one, the messages come from the
/// pool and are only good until its next reset (#640); without, they are new. The members set are the
/// same either way.
/// </remarks>
public static class ObjectStateWriter
{
    /// <summary>
    /// A world object entering a client's view. Placement is all such an object has, and all
    /// of it is sent whatever is marked changed, because the client has none of it yet.
    /// </summary>
    public static ObjectState From(IWorldObject worldObject, ObjectStatePool? pool = null)
    {
        ObjectState state = New(pool, worldObject.Guid.RawValue);
        state.Position = Of(pool, worldObject.Position);
        state.Velocity = Of(pool, worldObject.Velocity);
        state.Orientation = worldObject.Orientation.y;
        return state;
    }

    public static ObjectState From(IWorldObject worldObject, GameEntityFields fields, ObjectStatePool? pool = null)
    {
        ObjectState state = New(pool, worldObject.Guid.RawValue);

        AddPlacement(state, worldObject, fields, pool);

        return state;
    }

    public static ObjectState From(ICreature creature, GameEntityFields fields, ObjectStatePool? pool = null)
    {
        ObjectState state = FromUnit(creature, fields, pool);

        // Both go out whatever is marked changed. A creature's template never changes, and
        // its name is derived from it.
        state.CreatureMetadataId = creature.Metadata.Id.Value;
        state.Name = creature.Name;

        // Also whatever is marked changed, for the same reason: it is fixed at spawn, and a client
        // that first sees this creature on an update still needs it to offer a prompt. Sent only
        // as true; a creature that cannot be interacted with leaves the member out, which a client
        // reads as false. The value lives on the World-side Creature, not on ICreature, so no mod
        // can make a creature advertise an interaction the server would refuse. Any ICreature other
        // than Creature therefore never advertises the flag, and that is deliberate: it fails safe,
        // costing at most a prompt the client does not offer, and InteractHandler stays the
        // authority on whether an interact is accepted.
        if (creature is Creature { CanInteract: true })
        {
            state.CanInteract = true;
        }

        return state;
    }

    public static ObjectState From(ICharacter character, GameEntityFields fields, ObjectStatePool? pool = null)
    {
        ObjectState state = FromUnit(character, fields, pool);

        if (Has(fields, GameEntityFields.Experience))
        {
            state.Experience = character.Experience;
        }

        if (Has(fields, GameEntityFields.RequiredExperience))
        {
            state.RequiredExperience = character.RequiredExperience;
        }

        // Goes out whatever is marked changed: a character's name does not change while it is
        // in the world, and a client that missed it would have nothing to label it with.
        state.Name = character.Name;

        // Whatever is marked changed, only as true (#164): a client reads its absence on any character
        // state as off, so a flag that turned off is told by the next state leaving it out, which the
        // PvpEnabled dirty bit guarantees is sent. World-side value, so a mod cannot fake it.
        if (character is CharacterEntity { PvpEnabled: true })
        {
            state.PvpEnabled = true;
        }

        return state;
    }

    private static ObjectState FromUnit(IUnit unit, GameEntityFields fields, ObjectStatePool? pool)
    {
        ObjectState state = New(pool, unit.Guid.RawValue);

        AddPlacement(state, unit, fields, pool);

        if (Has(fields, GameEntityFields.MoveState))
        {
            state.MoveState = unit.MoveState;
        }

        if (Has(fields, GameEntityFields.Health))
        {
            state.Health = unit.Health;
        }

        if (Has(fields, GameEntityFields.CurrentHealth))
        {
            state.CurrentHealth = unit.CurrentHealth;
        }

        if (Has(fields, GameEntityFields.PowerType))
        {
            state.PowerType = unit.PowerType;

            // A unit that spends nothing has no pool to report, so neither amount is sent
            // however they are marked. The amounts themselves are optional on the unit, and
            // an absent one stays absent rather than becoming a zero.
            if (unit.PowerType != PowerType.None)
            {
                if (Has(fields, GameEntityFields.Power))
                {
                    state.Power = unit.Power;
                }

                if (Has(fields, GameEntityFields.CurrentPower))
                {
                    state.CurrentPower = unit.CurrentPower;
                }
            }
        }

        if (Has(fields, GameEntityFields.Level))
        {
            state.Level = unit.Level;
        }

        if (Has(fields, GameEntityFields.IsDead))
        {
            // Only a character has a death state. A creature reports alive, because the
            // selections that ask for this are shared between the two kinds.
            state.IsDead = (unit as ICharacter)?.IsDead ?? false;
        }

        return state;
    }

    private static void AddPlacement(ObjectState state, IWorldObject worldObject, GameEntityFields fields,
        ObjectStatePool? pool)
    {
        if (Has(fields, GameEntityFields.Position))
        {
            state.Position = Of(pool, worldObject.Position);
        }

        if (Has(fields, GameEntityFields.Velocity))
        {
            state.Velocity = Of(pool, worldObject.Velocity);
        }

        if (Has(fields, GameEntityFields.Orientation))
        {
            // Yaw alone. Nothing in the world leans, so the other two angles are not sent.
            state.Orientation = worldObject.Orientation.y;
        }
    }

    /// <summary>
    /// <see cref="Enum.HasFlag" />, without the boxing it costs in code the JIT has not optimised yet,
    /// so a broadcast allocates nothing per entity from its first tick (#640).
    /// </summary>
    private static bool Has(GameEntityFields fields, GameEntityFields flag) => (fields & flag) == flag;

    private static ObjectState New(ObjectStatePool? pool, ulong guid) =>
        pool?.State(guid) ?? new ObjectState { Guid = guid };

    private static Vec3 Of(ObjectStatePool? pool, Vector3 vector) =>
        pool?.Vector(vector) ?? new Vec3 { X = vector.x, Y = vector.y, Z = vector.z };
}
