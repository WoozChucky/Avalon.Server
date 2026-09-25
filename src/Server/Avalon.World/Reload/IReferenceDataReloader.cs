namespace Avalon.World.Reload;

/// <summary>
/// Reloads content areas without a restart. Lives in Avalon.World, not Avalon.World.Public:
/// reloading is a privileged operation, not part of the modding API.
/// </summary>
public interface IReferenceDataReloader
{
    /// <summary>
    /// Prepares each area off the tick thread, applies it at the start of the next world tick, and
    /// completes once all requested areas are live or have failed. Areas are independent: one
    /// failing does not stop the others, and a failed area is left exactly as it was.
    /// </summary>
    Task<ReloadReport> ReloadAsync(IReadOnlyList<ReloadArea> areas, CancellationToken ct = default);
}
