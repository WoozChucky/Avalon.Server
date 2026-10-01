using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Reload;

public sealed class ReferenceDataReloader(IWorld world, ILogger<ReferenceDataReloader> logger)
    : IReferenceDataReloader
{
    // One reload at a time, held from the database read until the patch is live on the tick: two overlapping
    // reloads of one area could otherwise apply an older read last, leaving stale data live under a newer save.
    // Nothing waiting here runs on the tick thread (the apply is completed by the tick, awaited by the caller).
    private readonly SemaphoreSlim _one = new(1, 1);

    public async Task<ReloadReport> ReloadAsync(IReadOnlyList<ReloadArea> areas, CancellationToken ct = default)
    {
        await _one.WaitAsync(ct);
        try
        {
            return await ReloadLockedAsync(areas, ct);
        }
        finally
        {
            _one.Release();
        }
    }

    private async Task<ReloadReport> ReloadLockedAsync(IReadOnlyList<ReloadArea> areas, CancellationToken ct)
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
