using System.Diagnostics;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Telemetry;

/// <summary>
/// One handler run, a value so a dispatch allocates nothing (#875). Dispose it in a <c>finally</c> when the handler
/// returns and call <see cref="Fail" /> if it threw, both on the same variable: a copy (another variable, a by-value
/// argument) holds state of its own, so <see cref="Fail" /> on it would leave the run recorded as ok, and disposing
/// the original and a copy would record the run twice.
/// </summary>
public struct PacketDispatch : IDisposable
{
    private readonly PacketDispatchTelemetry? _owner;
    private readonly NetworkPacketType _type;
    private readonly Activity? _activity;
    private readonly IDisposable? _scope;
    private readonly long _started;
    private bool _failed;
    private bool _disposed;

    internal PacketDispatch(PacketDispatchTelemetry owner, NetworkPacketType type, Activity? activity, IDisposable? scope)
    {
        _owner = owner;
        _type = type;
        _activity = activity;
        _scope = scope;
        _started = Stopwatch.GetTimestamp();
        _failed = false;
        _disposed = false;
    }

    public void Fail(Exception exception)
    {
        _failed = true;
        _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity?.AddException(exception);
    }

    public void Dispose()
    {
        if (_disposed || _owner is null)
            return;
        _disposed = true;

        _owner.Record(_type, Stopwatch.GetElapsedTime(_started).TotalMilliseconds, _failed);
        _activity?.SetTag("avalon.outcome", _failed ? "error" : "ok");
        _activity?.Dispose();
        _scope?.Dispose();
    }
}
