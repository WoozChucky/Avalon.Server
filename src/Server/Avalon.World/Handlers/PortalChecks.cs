using Avalon.Domain.World;
using Avalon.Network.Packets.World;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;

namespace Avalon.World.Handlers;

/// <summary>Why a portal refuses a character, whatever it asks of the portal; <see cref="None" /> when it does not.</summary>
internal enum PortalRefusal : byte
{
    None,

    /// <summary>No map has the id asked for (<c>MapNotFound</c>).</summary>
    UnknownMap,

    /// <summary>The character is in no live instance (<c>MapNotFound</c>): the server's own fault.</summary>
    NoInstance,

    /// <summary>The map the character is on has no portal to the one asked for (<c>MapNotFound</c>).</summary>
    NoPortal,

    /// <summary>The character is outside the portal's radius, by the server's position of it (<c>NotNearPortal</c>).</summary>
    NotNearPortal,

    LevelTooLow,
    LevelTooHigh,
}

/// <summary>
/// What <see cref="PortalChecks.Check" /> found: the refusal, if any, and what the checks read on the way, the target
/// map's template and the portal leading to it, and how far from it the character stands.
/// </summary>
internal readonly record struct PortalCheck(
    PortalRefusal Refusal, MapTemplate? Template, PortalInstance? Portal, float Distance)
{
    /// <summary>The answer a client is given for <see cref="Refusal" />: <c>Success</c> when there is none.</summary>
    public MapTransitionResult Result => Refusal switch
    {
        PortalRefusal.None => MapTransitionResult.Success,
        PortalRefusal.NotNearPortal => MapTransitionResult.NotNearPortal,
        PortalRefusal.LevelTooLow => MapTransitionResult.LevelTooLow,
        PortalRefusal.LevelTooHigh => MapTransitionResult.LevelTooHigh,
        _ => MapTransitionResult.MapNotFound,
    };
}

/// <summary>
/// The checks a portal makes of a character, alive and in the game, before anything of the request itself (#1104): a
/// portal entry (<see cref="EnterMapHandler" />, steps 2 to 6) and the portal's menu (<see cref="InstanceListHandler" />)
/// make the same ones, in the same order, and each answers and counts a refusal in its own way. Reads only; allocates
/// nothing.
/// </summary>
internal static class PortalChecks
{
    /// <summary>
    /// In order: the target map is one the world has; the character is in a live instance; that instance has a portal
    /// to the target; the character stands within its radius, on the ground; its level is within the map's band.
    /// </summary>
    public static PortalCheck Check(IWorld world, ICharacter character, ushort targetMap)
    {
        // 2. Resolve the current instance
        IMapInstance? currentInstance = world.InstanceRegistry.GetInstanceById(character.InstanceId);

        // 3. The target map's template, by id.
        MapTemplate? targetTemplate = world.MapTemplates.FindById(targetMap);
        if (targetTemplate == null)
        {
            return new PortalCheck(PortalRefusal.UnknownMap, null, null, 0f);
        }

        // 4. Find a portal on the current map that leads to the target. Every instance is built from a chunk layout,
        // and its portals are the PortalInstances PortalPlacementService placed from Layout.Portals as it was built:
        // there is no other source of portals.
        if (currentInstance is not MapInstance mi)
        {
            return new PortalCheck(PortalRefusal.NoInstance, targetTemplate, null, 0f);
        }

        PortalInstance? match = PortalTo(mi, targetMap);
        if (match is null)
        {
            return new PortalCheck(PortalRefusal.NoPortal, targetTemplate, null, 0f);
        }

        // 5. Proximity check: the server's position of the character against the portal's radius, on the ground: a
        // portal's height is where it is drawn, not how far a character standing at it is from it.
        float dx = character.Position.x - match.Position.x, dz = character.Position.z - match.Position.z;
        float distance = MathF.Sqrt((dx * dx) + (dz * dz));
        if (distance > match.Radius)
        {
            return new PortalCheck(PortalRefusal.NotNearPortal, targetTemplate, match, distance);
        }

        // 6. Level checks
        PortalRefusal refusal = PortalRefusal.None;
        if (targetTemplate.MinLevel.HasValue && character.Level < targetTemplate.MinLevel.Value)
        {
            refusal = PortalRefusal.LevelTooLow;
        }
        else if (targetTemplate.MaxLevel.HasValue && character.Level > targetTemplate.MaxLevel.Value)
        {
            refusal = PortalRefusal.LevelTooHigh;
        }

        return new PortalCheck(refusal, targetTemplate, match, distance);
    }

    /// <summary>The first portal of <paramref name="instance" /> that leads to <paramref name="targetMap" />, or null.</summary>
    private static PortalInstance? PortalTo(MapInstance instance, ushort targetMap)
    {
        IReadOnlyList<PortalInstance> portals = instance.Portals;
        for (int i = 0; i < portals.Count; i++)
        {
            if (portals[i].TargetMapId == targetMap)
            {
                return portals[i];
            }
        }

        return null;
    }
}
