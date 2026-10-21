namespace Avalon.World.Doodads;

/// <summary>
/// Why a doodad use was refused (DoodadUseService), in the order the checks run. Never sent: every refusal is a silent
/// drop, written at Debug. World-side.
/// </summary>
public enum DoodadUseRefusal : byte
{
    /// <summary>The user is dead.</summary>
    Dead,

    /// <summary>The user is stunned (#770).</summary>
    Stunned,

    /// <summary>No doodad here has the guid, or it is despawned.</summary>
    NotFound,

    /// <summary>The doodad is scenery: its template's interaction is None.</summary>
    NotInteractive,

    /// <summary>The user is farther than the template's interaction range, or either position is not a number.</summary>
    OutOfRange,

    /// <summary>The user may not see the doodad: its visibility gate, or its script, hides it from the user.</summary>
    NotVisible,

    /// <summary>Its usability gate refuses the user.</summary>
    NotUsable,

    /// <summary>A once-per-instance doodad that is not ready.</summary>
    Spent,

    /// <summary>The user is casting an ability.</summary>
    Busy,

    /// <summary>Its script's CanUse refused, or threw.</summary>
    ScriptRefused,

    /// <summary>The user's instance holds no doodads.</summary>
    NoHost,
}
