using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.World.Auras;
using Avalon.World.Doodads;
using Avalon.World.Entities;

namespace Avalon.World.Items;

/// <summary>
/// Whether a character may use a Resonance Workbench now, World-side and not on the wire (#1180 part C): the prefix every
/// Workbench request (identify, extract, imprint) passes before its own rules, which map it to their result.
/// </summary>
public enum WorkbenchAccess : byte
{
    /// <summary>The character may use the Workbench.</summary>
    Ok,

    /// <summary>The character is dead.</summary>
    Dead,

    /// <summary>The character cannot act (stunned).</summary>
    Stunned,

    /// <summary>No instance, no doodad of that guid in it, a despawned one, or one whose service is not Workbench.</summary>
    NotFound,

    /// <summary>The character is beyond the Workbench's interaction range, or its distance is not a number.</summary>
    TooFar,
}

/// <summary>
/// The checks every Workbench request makes first (#1180 part C), in order: the character is dead; it is stunned; the
/// Workbench is not found (no instance, no doodad of that guid in it, a despawned one, or one whose template's service is
/// not Workbench); the character is beyond the Workbench's interaction range (a position that is not a number is beyond
/// it). It reads and never changes, and allocates nothing. Doodad visibility is not asked: the catalog refuses a gate or
/// a script on a Workbench template, and a town spawn row carries no gate. Tick thread only.
/// </summary>
public static class WorkbenchRules
{
    /// <param name="workbench">The Workbench's guid as the request carries it.</param>
    public static WorkbenchAccess Access(CharacterEntity character, IDoodadHost? host, ulong workbench)
    {
        if (character.IsDead)
            return WorkbenchAccess.Dead;

        if (!ControlGates.CanAct(character))
            return WorkbenchAccess.Stunned;

        if (host is null || !host.Doodads.TryGet(workbench, out Doodad? doodad)
            || doodad.State == DoodadState.Despawned || doodad.Template.Service != DoodadService.Workbench)
        {
            return WorkbenchAccess.NotFound;
        }

        // Written so a distance that is not a number refuses. Asked on every request: a doodad has no conversation to
        // leash, so walking away after opening the window is caught here.
        return Vector3.Distance(character.Position, doodad.Position) <= doodad.Template.InteractionRange
            ? WorkbenchAccess.Ok
            : WorkbenchAccess.TooFar;
    }
}
