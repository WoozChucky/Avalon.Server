using System;
using System.Collections.Generic;
using Avalon.Common;
using Avalon.Network.Packets.Combat;
using Avalon.World.Public;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

/// <summary>
/// Per-tick service that mirrors each player's targeted hostile threat list back to the
/// owning connection via <c>SThreatListPacket</c>.
///
/// Throttling: a packet is only sent for a (connection, target) pair when the elapsed time
/// since the last broadcast exceeds <see cref="CombatConfig.ThreatBroadcastIntervalMs"/>
/// (default 250 ms) AND the top-attacker threat percentage has shifted by more than
/// <see cref="CombatConfig.ThreatBroadcastDeltaThreshold"/> (default 5 %). The service
/// always sends the first packet for a new (connection, target) pair regardless of
/// throttling so the client never starts with stale UI.
///
/// State is keyed by <see cref="IWorldConnection"/>. Entries are dropped as soon as the
/// player loses their target, the target leaves combat, or the target is no longer present
/// on the instance — preventing leaks on disconnect-via-no-target paths.
/// </summary>
public sealed class ThreatBroadcastService
{
    private readonly CombatConfig _config;
    private readonly Dictionary<IWorldConnection, BroadcastState> _state = new();

    public ThreatBroadcastService(CombatConfig config) => _config = config;

    /// <summary>
    /// Drops cached state for <paramref name="connection"/>. Call from
    /// <c>MapInstance.RemoveCharacter</c> so disconnected/transferred clients don't accumulate
    /// dictionary entries — the iteration in <see cref="Tick"/> only sees currently-connected
    /// clients, so a dropped connection's state would otherwise be unreachable but retained.
    /// </summary>
    public void Forget(IWorldConnection connection) => _state.Remove(connection);

    public void Tick(
        IReadOnlyCollection<IWorldConnection> connections,
        IReadOnlyDictionary<ObjectGuid, ICreature> creatures,
        ICombatService combatService)
    {
        DateTime now = DateTime.UtcNow;

        foreach (IWorldConnection conn in connections)
        {
            if (conn.Character is null || conn.Character.IsDead)
            {
                _state.Remove(conn);
                continue;
            }

            if (conn.CurrentTargetGuid is not { } rawTargetGuid)
            {
                _state.Remove(conn);
                continue;
            }

            var targetGuid = new ObjectGuid(rawTargetGuid);

            // Threat lists are only meaningful for hostile creatures. Player-on-player threat
            // is out of scope for V1 and creatures are the only ObjectType that can be a hostile.
            if (targetGuid.Type != ObjectType.Creature ||
                !creatures.TryGetValue(targetGuid, out ICreature? creature))
            {
                _state.Remove(conn);
                continue;
            }

            IEncounter? encounter = combatService.GetEncounterFor(creature);
            if (encounter is null)
            {
                _state.Remove(conn);
                continue;
            }

            IReadOnlyDictionary<IUnit, float> threats = encounter.GetThreatList(creature);
            if (threats.Count == 0)
            {
                _state.Remove(conn);
                continue;
            }

            float total = 0f;
            foreach (float t in threats.Values)
                total += t;

            if (total <= 0f)
                continue;

            ThreatEntry[] entries = new ThreatEntry[threats.Count];
            int    i          = 0;
            float  topPercent = 0f;
            foreach ((IUnit attacker, float threat) in threats)
            {
                float percent = threat / total;
                entries[i++] = new ThreatEntry
                {
                    AttackerGuid  = attacker.Guid.RawValue,
                    ThreatPercent = percent,
                };
                if (percent > topPercent)
                    topPercent = percent;
            }

            // Throttle. We only suppress a re-broadcast when ALL of:
            //   (a) the same target is still selected (so target switches always send),
            //   (b) the throttle window has not elapsed,
            //   (c) the top-attacker share hasn't shifted by more than the configured delta.
            // (a) is required so an "untarget → retarget same creature" path still resends.
            if (_state.TryGetValue(conn, out BroadcastState prev)
                && ReferenceEquals(prev.Target, creature)
                && (now - prev.LastSent).TotalMilliseconds < _config.ThreatBroadcastIntervalMs
                && Math.Abs(topPercent - prev.LastTopPercent) < _config.ThreatBroadcastDeltaThreshold)
            {
                continue;
            }

            conn.Send(SThreatListPacket.Create(targetGuid, entries, conn.CryptoSession.Encrypt));
            _state[conn] = new BroadcastState(creature, now, topPercent);
        }
    }

    private readonly record struct BroadcastState(IUnit Target, DateTime LastSent, float LastTopPercent);
}
