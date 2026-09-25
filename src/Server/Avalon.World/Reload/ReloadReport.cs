namespace Avalon.World.Reload;

/// <summary>One area's reload outcome: what changed on success, or why it failed.</summary>
public sealed record ReloadOutcome(ReloadArea Area, bool Succeeded, string Summary, TimeSpan Elapsed, Exception? Error);

/// <summary>The whole reload's outcomes, one per requested area, in request order.</summary>
public sealed record ReloadReport(IReadOnlyList<ReloadOutcome> Outcomes);
