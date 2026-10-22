using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;
using Avalon.World.Abilities;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;

using WireDoodadState = Avalon.Network.Packets.State.DoodadState;
using WireRarity = Avalon.Network.Packets.State.CreatureRarity;
using WorldDoodadState = Avalon.World.Doodads.DoodadState;
using WorldRarity = Avalon.World.Public.Enums.CreatureRarity;

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
    /// of it is sent whatever is marked changed, because the client has none of it yet. A projectile
    /// adds what fired it (#479), fixed for its flight.
    /// </summary>
    public static ObjectState From(IWorldObject worldObject, ObjectStatePool? pool = null)
    {
        ObjectState state = New(pool, worldObject.Guid.RawValue);
        state.Position = Of(pool, worldObject.Position);
        state.Velocity = Of(pool, worldObject.Velocity);
        state.Orientation = worldObject.Orientation.y;
        if (worldObject is IProjectileIdentity projectile)
        {
            state.AbilityId = projectile.AbilityId;
            state.CasterGuid = projectile.CasterGuid;
            state.CastId = projectile.CastId;
        }

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

        // The three below live on the World-side Creature, not on ICreature, so no mod can change what a
        // creature advertises. Any ICreature other than Creature therefore sends none of them, and that is
        // deliberate: it fails safe, costing at most a prompt the client does not offer, a Normal nameplate or
        // the default model, and InteractHandler stays the authority on whether an interact is accepted.
        if (creature is Creature spawned)
        {
            // Also whatever is marked changed, for the same reason: it is fixed at spawn, and a client that first
            // sees this creature on an update still needs it to offer a prompt. Sent only as true; a creature that
            // cannot be interacted with leaves the member out, which a client reads as false.
            if (spawned.CanInteract)
            {
                state.CanInteract = true;
            }

            // Whatever is marked changed too, and only when not Normal (#709): the rarity is fixed at spawn, a
            // client that first sees the creature on an update still needs it for the nameplate, and a Normal
            // creature, most of them, pays nothing, since absent means Normal.
            if (spawned.Rarity != WorldRarity.Normal)
            {
                state.Rarity = ToWire(spawned.Rarity);
            }

            // The model the client draws (#1022), on the same terms as the rarity: whatever is marked changed, and
            // only when not 0, since absent means none and a creature without one pays nothing.
            if (spawned.DisplayId != 0)
            {
                state.DisplayId = spawned.DisplayId;
            }
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

        // Goes out whatever is marked changed, like the name: both are fixed for the character's life, and a
        // client that first sees it on an update needs them to pick the character's model.
        state.Class = character.Class;
        state.Gender = character.Gender;

        // Whatever is marked changed, only as true (#164): a client reads its absence on any character
        // state as off, so a flag that turned off is told by the next state leaving it out, which the
        // PvpEnabled dirty bit guarantees is sent. World-side value, so a mod cannot fake it.
        if (character is CharacterEntity { PvpEnabled: true })
        {
            state.PvpEnabled = true;
        }

        return state;
    }

    /// <summary>
    /// A doodad, for one recipient: <paramref name="canInteract" /> is that recipient's answer, so the entry is never
    /// shared with another. An add (<paramref name="fields" /> include <see cref="GameEntityFields.Position" />) carries
    /// its placement, its template and its state, and <see cref="ObjectState.CanInteract" /> only as true; an update
    /// carries only what is marked, <see cref="GameEntityFields.CanInteract" /> as its value, false included, so a client
    /// told a doodad is usable is also told when it stops being. A despawned doodad is never described, so its state is
    /// left out if one is ever asked for.
    /// </summary>
    public static ObjectState From(Doodad doodad, GameEntityFields fields, bool canInteract, ObjectStatePool? pool = null)
    {
        ObjectState state = New(pool, doodad.Guid.RawValue);

        if (Has(fields, GameEntityFields.Position))
        {
            // A doodad never moves, so its placement goes out once, on its add, with what never changes after it.
            state.Position = Of(pool, doodad.Position);
            state.Orientation = doodad.Orientation.y;
            state.DoodadTemplateId = doodad.Template.Id.Value;
            state.DoodadState = ToWire(doodad.State);
            if (canInteract)
            {
                state.CanInteract = true;
            }

            return state;
        }

        if (Has(fields, GameEntityFields.DoodadState))
        {
            state.DoodadState = ToWire(doodad.State);
        }

        if (Has(fields, GameEntityFields.CanInteract))
        {
            state.CanInteract = canInteract;
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
        }

        // Amounts change independently of the type (spending, regeneration, gear). An unchanged type
        // must not suppress those deltas. A unit that spends nothing still reports neither amount.
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

        if (Has(fields, GameEntityFields.Level))
        {
            state.Level = unit.Level;
        }

        if (Has(fields, GameEntityFields.IsDead))
        {
            // A character keeps its own flag, since it is revived and dies again. A creature is dead
            // at 0 health, the rule every server check on a creature already uses (the hit query, the
            // heal, the kill, the death broadcast), so its state cannot disagree with them (#672). It
            // reads only health, so no ICreature, a mod's included, can report a death it has not had.
            state.IsDead = unit is ICharacter character ? character.IsDead : unit.CurrentHealth == 0;
        }

        // The ward and its maximum travel together (#1178): a change to either sends both, so a client always holds the pair
        // its ward bar is drawn from. A unit with no ward pays only on its add and when it loses its ward.
        if (Has(fields, GameEntityFields.Ward) || Has(fields, GameEntityFields.MaxWard))
        {
            (uint ward, uint maxWard) = WardOf(unit);
            state.Ward = ward;
            state.MaxWard = maxWard;
        }

        return state;
    }

    /// <summary>
    /// What a unit's ward holds and what a full one holds (#1178). Both live World-side, not on IUnit, so no mod can change
    /// what a unit advertises; a unit that is neither a character nor a creature has no ward.
    /// </summary>
    private static (uint Ward, uint MaxWard) WardOf(IUnit unit) => unit switch
    {
        CharacterEntity character => (character.Ward, character.MaxWard),
        Creature creature => (creature.Ward, creature.MaxWard),
        _ => (0u, 0u),
    };

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
    /// The server's rarity as the wire's (#709), value by value, so a renumber on either side cannot reach a
    /// client unnoticed. A value the wire does not know is left out, which a client reads as Normal.
    /// </summary>
    public static WireRarity? ToWire(WorldRarity rarity) => rarity switch
    {
        WorldRarity.Normal => WireRarity.Normal,
        WorldRarity.Elite => WireRarity.Elite,
        WorldRarity.Rare => WireRarity.Rare,
        WorldRarity.Boss => WireRarity.Boss,
        _ => null,
    };

    /// <summary>
    /// The server's doodad state as the wire's, value by value. Despawned has no wire value, as a despawned doodad is told
    /// by a remove: left out.
    /// </summary>
    public static WireDoodadState? ToWire(WorldDoodadState doodadState) => doodadState switch
    {
        WorldDoodadState.Ready => WireDoodadState.Ready,
        WorldDoodadState.Used => WireDoodadState.Used,
        _ => null,
    };

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
