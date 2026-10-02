using Avalon.Infrastructure.Presence;
using Avalon.World.Configuration;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Presence;

/// <summary>
/// Takes the live presence snapshot (which instance holds which characters, and a few fields of each) for the admin
/// view, on the tick (#639). <c>WorldServer.Update</c> calls <see cref="CaptureIfDue" /> in the serial phase after the
/// world update, every tick; it captures about once a second (<see cref="Interval" />) by the container's
/// <see cref="TimeProvider" />, with no timer, and hands the snapshot over through one volatile reference. The host's
/// <c>PresenceSnapshotService</c> takes it (<see cref="Take" />) and writes Redis off the tick.
/// </summary>
/// <remarks>
/// Strictly a reader of simulation state. It reads the rosters on the tick that changes them, so it no longer races
/// a character entering or leaving, and never reads a torn position. A snapshot is taken by the writer once: a
/// stalled tick captures nothing new, the writer writes nothing, and the keys expire on their TTL, as they should.
/// A DI singleton, World-side; its cost is a few objects per player once a second.
/// </remarks>
public sealed class PresenceCapture
{
    /// <summary>The least time between two captures.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly ushort _worldId;
    private readonly ILogger<PresenceCapture> _logger;
    private readonly TimeProvider _time;
    private readonly ThrottledErrorLog _instanceErrors;

    // Tick thread only.
    private DateTimeOffset? _due;

    // Written on the tick, taken by the writer on its own thread.
    private WorldPresenceSnapshot? _latest;

    public PresenceCapture(IOptions<GameConfiguration> configuration, ILogger<PresenceCapture> logger,
        TimeProvider? time = null)
    {
        _worldId = configuration.Value.WorldId.Value;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _instanceErrors = new ThrottledErrorLog(logger, _time, "The presence capture of an instance");
    }

    /// <summary>
    /// Captures when the interval since the last capture has passed (the first call captures at once). A late tick
    /// captures once, and the next capture is due an interval after it. Tick thread only.
    /// </summary>
    public void CaptureIfDue(IInstanceRegistry registry)
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (_due is { } due && now < due)
            return;

        _due = now + Interval;

        DateTime capturedAt = now.UtcDateTime;
        List<InstancePresenceSnapshot> instances = [];
        foreach (IMapInstance instance in registry.ActiveInstances)
        {
            InstancePresenceSnapshot? snapshot = TryCapture(instance, capturedAt);
            if (snapshot is not null)
                instances.Add(snapshot);
        }

        // Nobody in the world: nothing to write, as before, and a snapshot the writer has not taken yet is dropped, so
        // it is never written as live after its players have gone. What was written last expires on its TTL.
        if (instances.Count == 0)
        {
            Volatile.Write(ref _latest, null);
            return;
        }

        Volatile.Write(ref _latest,
            new WorldPresenceSnapshot(_worldId, capturedAt, instances, WorldPresenceSnapshot.CurrentVersion));
    }

    /// <summary>The latest snapshot not yet taken, or none; each one is taken once. Any thread.</summary>
    public WorldPresenceSnapshot? Take() => Interlocked.Exchange(ref _latest, null);

    /// <summary>One instance's snapshot, or none when it holds no player or its roster could not be read.</summary>
    private InstancePresenceSnapshot? TryCapture(IMapInstance instance, DateTime capturedAt)
    {
        try
        {
            List<CharacterPresenceSnapshot> characters = [];
            foreach (ICharacter c in instance.Characters.Values)
            {
                characters.Add(new CharacterPresenceSnapshot(
                    CharacterId: c.Guid.Id,
                    Name: c.Name,
                    Class: c.Class.ToString(),
                    X: c.Position.x, Y: c.Position.y, Z: c.Position.z,
                    Orientation: c.Orientation.y,
                    Level: c.Level,
                    CurrentHealth: c.CurrentHealth,
                    Health: c.Health,
                    MoveState: c.MoveState.ToString(),
                    InCombat: c.IsInCombat,
                    Dead: c.IsDead,
                    LastSeen: capturedAt));
            }

            if (characters.Count == 0)
                return null;

            return new InstancePresenceSnapshot(
                InstanceId: instance.InstanceId,
                TemplateId: instance.TemplateId.Value,
                Seed: instance.Seed,
                MapType: instance.MapType.ToString(),
                ConfigVersion: instance.ConfigVersion,
                OwnerCharacterId: instance.OwnerCharacterId,
                Characters: characters);
        }
        catch (Exception e)
        {
            // On the tick a roster no longer changes mid-walk, so a throw here is a fault: logged, at most once per
            // 10 s, and the instance is left out of this snapshot only.
            _instanceErrors.Failed(e);
            _logger.LogDebug("Presence capture left out instance {InstanceId}", instance.InstanceId);
            return null;
        }
    }
}
