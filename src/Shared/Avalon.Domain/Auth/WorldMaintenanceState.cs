namespace Avalon.Domain.Auth;

public sealed record WorldMaintenanceState(bool Enabled, long Revision, DateTime? DeadlineUtc)
{
    /// <summary>A missing deadline on an enabled row fails closed.</summary>
    public bool IsCutoffActive(DateTime nowUtc)
        => Enabled && (DeadlineUtc is null || nowUtc >= DeadlineUtc.Value);
}
