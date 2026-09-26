using System.Diagnostics.Metrics;
using Avalon.Common.Telemetry;

namespace Avalon.Server.Auth.UnitTests.Handlers;

/// <summary>
/// The login counter is static and shared, and other test classes run in parallel. The probe keeps
/// only the measurements made on this test's async flow.
/// </summary>
internal sealed class LoginCounterProbe : IDisposable
{
    private static readonly AsyncLocal<LoginCounterProbe?> Current = new();
    private readonly MeterListener _listener = new();

    public List<string> Results { get; } = [];

    public LoginCounterProbe()
    {
        Current.Value = this;
        _listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter == DiagnosticsConfig.Auth.Meter && instrument.Name == "avalon.auth.logins")
                l.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (Current.Value != this) return;
            foreach (KeyValuePair<string, object?> tag in tags)
                if (tag.Key == "result") Results.Add((string)tag.Value!);
        });
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();
}
