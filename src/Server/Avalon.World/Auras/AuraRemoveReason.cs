namespace Avalon.World.Auras;

/// <summary>Why an aura ended. World-side: an AuraScript's OnRemove is told it.</summary>
public enum AuraRemoveReason
{
    /// <summary>Its time ran out, or its template is no longer loaded.</summary>
    Expired = 1,

    /// <summary>Its unit died: death ends every aura.</summary>
    Death = 2,

    /// <summary>Its owner cancelled it (helpful auras only), or a GameMaster's /aura clear ended it (harmful ones).</summary>
    Cancelled = 3,

    /// <summary>Its script ended it.</summary>
    Script = 4,

    /// <summary>A creature turned for home after a fight: its harmful auras end with the fight.</summary>
    Reset = 5,

    /// <summary>
    /// A crowd-control aura ended early by chance (#770): when its unit took damage, or on its own, once per second
    /// held, by its row's break chances.
    /// </summary>
    Broken = 6,
}
