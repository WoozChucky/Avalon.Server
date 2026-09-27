namespace Avalon.Server.Auth;

/// <summary>What <see cref="WorldSelectBudget.Take"/> decided for one world select (#574).</summary>
public enum WorldSelectAdmission
{
    /// <summary>Within the cap: the select goes on as usual.</summary>
    Admitted,

    /// <summary>Past the cap, and the first refusal of this window: log it, then close.</summary>
    RefusedFirstInWindow,

    /// <summary>Past the cap, already logged this window: close without logging again.</summary>
    Refused,
}
