namespace Avalon.World.Auras;

/// <summary>Why an aura ended. World-side: an AuraScript's OnRemove is told it.</summary>
public enum AuraRemoveReason
{
    /// <summary>Its time ran out, or its template is no longer loaded.</summary>
    Expired = 1,

    /// <summary>Its unit died: death ends every aura.</summary>
    Death = 2,

    /// <summary>Its owner cancelled it (helpful auras only).</summary>
    Cancelled = 3,

    /// <summary>Its script ended it.</summary>
    Script = 4,

    /// <summary>A creature turned for home after a fight: its harmful auras end with the fight.</summary>
    Reset = 5,
}
