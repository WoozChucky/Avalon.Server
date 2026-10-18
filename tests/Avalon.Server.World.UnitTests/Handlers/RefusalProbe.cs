using System.Diagnostics.Metrics;
using Avalon.Common.Telemetry;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// The reasons counted in <c>world.portal_entry.refusals</c> on this test's own flow: the counter is static and
/// shared, and other test classes refuse portal entries in parallel.
/// </summary>
internal sealed class RefusalProbe : IDisposable
{
    private static readonly AsyncLocal<RefusalProbe?> s_current = new();
    private readonly MeterListener _listener = new();

    public RefusalProbe()
    {
        s_current.Value = this;
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == DiagnosticsConfig.World.Meter && instrument.Name == "world.portal_entry.refusals")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (s_current.Value != this)
                return;

            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key == "reason")
                    Reasons.Add((string)tag.Value!);
            }
        });
        _listener.Start();
    }

    public List<string> Reasons { get; } = [];

    public void Dispose() => _listener.Dispose();
}
