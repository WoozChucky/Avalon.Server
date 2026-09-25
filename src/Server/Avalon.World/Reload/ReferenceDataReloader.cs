using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Reload;

public sealed class ReferenceDataReloader(IWorld world, ILogger<ReferenceDataReloader> logger)
    : IReferenceDataReloader
{
    public async Task<ReloadReport> ReloadAsync(IReadOnlyList<ReloadArea> areas, CancellationToken ct = default)
    {
        var outcomes = new List<ReloadOutcome>(areas.Count);

        foreach (ReloadArea area in areas)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                StaticDataPatch patch = await world.Data.PrepareAsync(area, ct);
                await world.Data.ApplyOnNextTickAsync(patch);

                outcomes.Add(new ReloadOutcome(area, true, patch.Describe(), stopwatch.Elapsed, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Only the caller's own token should unwind ReloadAsync as a cancellation. Any
                // other OperationCanceledException (e.g. a database timeout surfacing as a task
                // cancellation) falls through to the catch below and is recorded as an ordinary
                // area failure instead. Precedent: PresenceSnapshotService.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reload of {Area} failed; the previous data is still live", area);
                outcomes.Add(new ReloadOutcome(area, false, string.Empty, stopwatch.Elapsed, ex));
            }
        }

        return new ReloadReport(outcomes);
    }
}
