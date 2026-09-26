// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Telemetry;

/// <summary>One handler run. Dispose it when the handler returns; call <see cref="Fail" /> if it threw.</summary>
public sealed class PacketDispatch : IDisposable
{
    private readonly PacketDispatchTelemetry _owner;
    private readonly NetworkPacketType _type;
    private readonly Activity? _activity;
    private readonly IDisposable? _scope;
    private readonly long _started = Stopwatch.GetTimestamp();
    private bool _failed;
    private bool _disposed;

    internal PacketDispatch(PacketDispatchTelemetry owner, NetworkPacketType type, Activity? activity, IDisposable? scope)
    {
        _owner = owner;
        _type = type;
        _activity = activity;
        _scope = scope;
    }

    public void Fail(Exception exception)
    {
        _failed = true;
        _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity?.AddException(exception);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _owner.Record(_type, Stopwatch.GetElapsedTime(_started).TotalMilliseconds, _failed);
        _activity?.SetTag("avalon.outcome", _failed ? "error" : "ok");
        _activity?.Dispose();
        _scope?.Dispose();
    }
}
