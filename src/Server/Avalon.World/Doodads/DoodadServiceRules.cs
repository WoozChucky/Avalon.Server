using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.World.Auras;
using Avalon.World.Entities;

namespace Avalon.World.Doodads;

/// <summary>
/// Whether a character may use a service doodad now, World-side and not on the wire (#1180 part C, #1181): the prefix
/// every service request (the Workbench's identify, extract and imprint; the Lectern's refund) passes before its own
/// rules, which map it to their result.
/// </summary>
public enum DoodadServiceAccess : byte
{
    /// <summary>The character may use the doodad.</summary>
    Ok,

    /// <summary>The character is dead.</summary>
    Dead,

    /// <summary>The character cannot act (stunned).</summary>
    Stunned,

    /// <summary>No instance, no doodad of that guid in it, a despawned one, or one whose service is not the one asked.</summary>
    NotFound,

    /// <summary>The character is beyond the doodad's interaction range, or its distance is not a number.</summary>
    TooFar,
}

/// <summary>
/// The checks every service request makes first (#1180 part C, #1181), in order: the character is dead; it is stunned;
/// the doodad is not found (no instance, no doodad of that guid in it, a despawned one, or one whose template's service
/// is not the one asked); the character is beyond the doodad's interaction range (a position that is not a number is
/// beyond it). It reads and never changes, and allocates nothing. Doodad visibility is not asked: the catalog refuses a
/// gate or a script on a service doodad's template, and a town spawn row carries no gate. Tick thread only.
/// </summary>
public static class DoodadServiceRules
{
    /// <param name="character">The character making the request.</param>
    /// <param name="host">The character's instance, or null when it has none.</param>
    /// <param name="doodad">The doodad's guid as the request carries it.</param>
    /// <param name="service">The service the request needs the doodad to offer.</param>
    public static DoodadServiceAccess Access(CharacterEntity character, IDoodadHost? host, ulong doodad,
        DoodadService service)
    {
        if (character.IsDead)
            return DoodadServiceAccess.Dead;

        if (!ControlGates.CanAct(character))
            return DoodadServiceAccess.Stunned;

        if (host is null || !host.Doodads.TryGet(doodad, out Doodad? found)
            || found.State == DoodadState.Despawned || found.Template.Service != service)
        {
            return DoodadServiceAccess.NotFound;
        }

        // Written so a distance that is not a number refuses. Asked on every request: a doodad has no conversation to
        // leash, so walking away after opening the window is caught here.
        return Vector3.Distance(character.Position, found.Position) <= found.Template.InteractionRange
            ? DoodadServiceAccess.Ok
            : DoodadServiceAccess.TooFar;
    }
}
