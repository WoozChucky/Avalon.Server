using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// One scenario run. <see cref="BytesPerWindow" /> is the gated figure (tick-thread bytes, the least of the measured
/// windows); everything else describes the timing phase, never gates, and is zero when the run had no timing phase.
/// </summary>
/// <param name="BytesPerWindow">The least tick-thread bytes allocated over one <see cref="ScenarioMeasurement.WindowTicks" />-tick window.</param>
/// <param name="BytesPerPlayerPerTick"><see cref="BytesPerWindow" /> per tick per player.</param>
/// <param name="TotalAllocatedBytes">Every thread's bytes over the timing phase (informational: other threads count too).</param>
/// <param name="TicksOverBudgetPercent">The share of timed ticks longer than one 60 Hz tick (16.7 ms).</param>
/// <param name="GcPausePercent">The share of the timing phase's wall time spent in GC pauses.</param>
public sealed record ScenarioReport(
    string Scenario, int Players, int Instances,
    long BytesPerWindow, double BytesPerPlayerPerTick,
    double BytesPerTickMean, long BytesPerTickP95, long BytesPerTickMax, long TotalAllocatedBytes,
    double TickMsMean, double TickMsP50, double TickMsP95, double TickMsP99, double TickMsMax, double TicksOverBudgetPercent,
    int Gen0, int Gen1, int Gen2, double GcPauseMs, double GcPausePercent,
    RuntimeStamp Runtime)
{
    /// <summary>How reports and the allocation baseline are written and read: camelCase, indented, <c>\n</c> line ends.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
    };
}

/// <summary>The runtime a report was measured on, so reports from different setups are not compared blindly.</summary>
public sealed record RuntimeStamp(bool ServerGc, string LatencyMode, int ProcessorCount, string Runtime, string? Commit)
{
    private static readonly TimeSpan s_gitTimeout = TimeSpan.FromSeconds(2);

    /// <summary>This process's runtime, and the commit it was built from when one can be found.</summary>
    public static RuntimeStamp Current() => new(GCSettings.IsServerGC, GCSettings.LatencyMode.ToString(),
        Environment.ProcessorCount, RuntimeInformation.FrameworkDescription, FindCommit());

    /// <summary>CI's <c>GITHUB_SHA</c>, else <c>git rev-parse --short HEAD</c>; null when neither answers. Never throws.</summary>
    private static string? FindCommit()
    {
        string? sha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(sha))
            return sha.Trim();

        try
        {
            using var git = new Process();
            git.StartInfo = new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            git.Start();

            // Both pipes drained as git runs, so a full one never stalls it.
            Task<string> output = git.StandardOutput.ReadToEndAsync();
            Task<string> errors = git.StandardError.ReadToEndAsync();
            if (!git.WaitForExit(s_gitTimeout))
            {
                git.Kill(entireProcessTree: true);
                return null;
            }

            if (git.ExitCode != 0 || !Task.WaitAll([output, errors], s_gitTimeout))
                return null;

            string commit = output.Result.Trim();
            return commit.Length > 0 ? commit : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
