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
/// </remarks>
public static class ObjectStateWriter
{
    /// <summary>
    /// A world object entering a client's view. Placement is all such an object has, and all
    /// of it is sent whatever is marked changed, because the client has none of it yet.
    /// </summary>
    public static ObjectState From(IWorldObject worldObject) => new()
    {
        Guid = worldObject.Guid.RawValue,
        Position = Of(worldObject.Position),
        Velocity = Of(worldObject.Velocity),
        Orientation = worldObject.Orientation.y,
    };

    public static ObjectState From(IWorldObject worldObject, GameEntityFields fields)
    {
        var state = new ObjectState { Guid = worldObject.Guid.RawValue };

        AddPlacement(state, worldObject, fields);

        return state;
    }

    public static ObjectState From(ICreature creature, GameEntityFields fields)
    {
        ObjectState state = FromUnit(creature, fields);

        // Both go out whatever is marked changed. A creature's template never changes, and
        // its name is derived from it.
        state.CreatureMetadataId = creature.Metadata.Id.Value;
        state.Name = creature.Name;

        return state;
    }

    public static ObjectState From(ICharacter character, GameEntityFields fields)
    {
        ObjectState state = FromUnit(character, fields);

        if (fields.HasFlag(GameEntityFields.Experience))
        {
            state.Experience = character.Experience;
        }

        if (fields.HasFlag(GameEntityFields.RequiredExperience))
        {
            state.RequiredExperience = character.RequiredExperience;
        }

        // Goes out whatever is marked changed: a character's name does not change while it is
        // in the world, and a client that missed it would have nothing to label it with.
        state.Name = character.Name;

        return state;
    }

    /// <summary>
    /// A portal, which shares only its position with the other kinds and has three members
    /// nothing else sets.
    /// </summary>
    public static ObjectState From(PortalInstance portal) => new()
    {
        Guid = portal.Guid.RawValue,
        Position = Of(portal.Position),
        PortalRadius = portal.Radius,
        PortalTargetMapId = portal.TargetMapId,
        PortalRole = portal.Role,
    };

    private static ObjectState FromUnit(IUnit unit, GameEntityFields fields)
    {
        var state = new ObjectState { Guid = unit.Guid.RawValue };

        AddPlacement(state, unit, fields);

        if (fields.HasFlag(GameEntityFields.MoveState))
        {
            state.MoveState = unit.MoveState;
        }

        if (fields.HasFlag(GameEntityFields.Health))
        {
            state.Health = unit.Health;
        }

        if (fields.HasFlag(GameEntityFields.CurrentHealth))
        {
            state.CurrentHealth = unit.CurrentHealth;
        }

        if (fields.HasFlag(GameEntityFields.PowerType))
        {
            state.PowerType = unit.PowerType;

            // A unit that spends nothing has no pool to report, so neither amount is sent
            // however they are marked. The amounts themselves are optional on the unit, and
            // an absent one stays absent rather than becoming a zero.
            if (unit.PowerType != PowerType.None)
            {
                if (fields.HasFlag(GameEntityFields.Power))
                {
                    state.Power = unit.Power;
                }

                if (fields.HasFlag(GameEntityFields.CurrentPower))
                {
                    state.CurrentPower = unit.CurrentPower;
                }
            }
        }

        if (fields.HasFlag(GameEntityFields.Level))
        {
            state.Level = unit.Level;
        }

        if (fields.HasFlag(GameEntityFields.IsDead))
        {
            // Only a character has a death state. A creature reports alive, because the
            // selections that ask for this are shared between the two kinds.
            state.IsDead = (unit as ICharacter)?.IsDead ?? false;
        }

        return state;
    }

    private static void AddPlacement(ObjectState state, IWorldObject worldObject, GameEntityFields fields)
    {
        if (fields.HasFlag(GameEntityFields.Position))
        {
            state.Position = Of(worldObject.Position);
        }

        if (fields.HasFlag(GameEntityFields.Velocity))
        {
            state.Velocity = Of(worldObject.Velocity);
        }

        if (fields.HasFlag(GameEntityFields.Orientation))
        {
            // Yaw alone. Nothing in the world leans, so the other two angles are not sent.
            state.Orientation = worldObject.Orientation.y;
        }
    }

    private static Vec3 Of(Vector3 vector) => new() { X = vector.x, Y = vector.y, Z = vector.z };
}
