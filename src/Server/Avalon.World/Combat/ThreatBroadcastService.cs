using Avalon.Common;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
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
/// A list is mirrored only while its target is in the viewer's view, as of the last decision of
/// that view (<see cref="CharacterCharacterGameState.Knows(ulong)"/>, #1269): a target that has left
/// it keeps the client's selection but gets no list until it is back, when the first is sent as for
/// a new pair.
///
/// State is keyed by <see cref="IWorldConnection"/>. Entries are dropped as soon as the
/// player loses their target, the target leaves combat or the viewer's view, or the target is no
/// longer present on the instance — preventing leaks on disconnect-via-no-target paths.
/// </summary>
public sealed class ThreatBroadcastService
{
    private readonly CombatConfig _config;
    private readonly TimeProvider _time;
    private readonly Dictionary<IWorldConnection, BroadcastState> _state = new();

    // The key a selected creature is looked up by, rewritten for each lookup: ObjectGuid is a class, and one built per
    // connection per tick was an allocation every tick for a target that gets no list. Tick thread only, never stored.
    private readonly ObjectGuid _creatureKey = new();

    /// <param name="time">The instance's clock, the one the rest of the world times by (#614).</param>
    public ThreatBroadcastService(CombatConfig config, TimeProvider? time = null)
    {
        _config = config;
        _time = time ?? TimeProvider.System;
    }

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
        DateTime now = _time.GetUtcNow().UtcDateTime;

        // The instance hands its dictionary's values: loop them with their struct enumerator, so the
        // tick allocates nothing (an interface foreach boxes it). Anything else takes the general path.
        if (connections is Dictionary<ObjectGuid, IWorldConnection>.ValueCollection values)
        {
            foreach (IWorldConnection conn in values)
                TickConnection(conn, creatures, combatService, now);
            return;
        }

        foreach (IWorldConnection conn in connections)
            TickConnection(conn, creatures, combatService, now);
    }

    private void TickConnection(IWorldConnection conn, IReadOnlyDictionary<ObjectGuid, ICreature> creatures,
        ICombatService combatService, DateTime now)
    {
        if (conn.Character is not CharacterEntity { IsDead: false } viewer)
        {
            _state.Remove(conn);
            return;
        }

        if (conn.CurrentTargetGuid is not { } rawTargetGuid)
        {
            _state.Remove(conn);
            return;
        }

        // Threat lists are only meaningful for hostile creatures. Player-on-player threat
        // is out of scope for V1 and creatures are the only ObjectType that can be a hostile.
        // Asked of the raw guid, as the view is below: no ObjectGuid is built for a target (#1269).
        if (ObjectGuid.TypeOf(rawTargetGuid) != ObjectType.Creature)
        {
            _state.Remove(conn);
            return;
        }

        // Only a target the client holds (#1269).
        if (!viewer.CharacterGameState.Knows(rawTargetGuid))
        {
            _state.Remove(conn);
            return;
        }

        // The view holds this guid, so it is a creature's, whose id fits the key; the check keeps any other out.
        _creatureKey.Set(ObjectType.Creature, (uint)rawTargetGuid);
        if (_creatureKey.RawValue != rawTargetGuid || !creatures.TryGetValue(_creatureKey, out ICreature? creature))
        {
            _state.Remove(conn);
            return;
        }

        IEncounter? encounter = combatService.GetEncounterFor(creature);
        if (encounter is null)
        {
            _state.Remove(conn);
            return;
        }

        IReadOnlyDictionary<IUnit, float> threats = encounter.GetThreatList(creature);
        if (threats.Count == 0)
        {
            _state.Remove(conn);
            return;
        }

        Measure(threats, out float total, out float topThreat);
        if (total <= 0f)
            return;

        float topPercent = Math.Max(0f, topThreat / total);

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
            return;
        }

        // Only a packet that is sent pays for its entries.
        var entries = new ThreatEntry[threats.Count];
        int i = 0;
        foreach ((IUnit attacker, float threat) in threats)
        {
            entries[i++] = new ThreatEntry
            {
                AttackerGuid = attacker.Guid.RawValue,
                ThreatPercent = threat / total,
            };
        }

        conn.Send(SThreatListPacket.Create(creature.Guid, entries, PacketEncoder.Shared));
        _state[conn] = new BroadcastState(creature, now, topPercent);
    }

    /// <summary>
    /// The threat list's sum and largest entry. An encounter's list is a <see cref="Dictionary{TKey,TValue}" />,
    /// looped with its struct enumerator so a throttled tick allocates nothing.
    /// </summary>
    private static void Measure(IReadOnlyDictionary<IUnit, float> threats, out float total, out float top)
    {
        total = 0f;
        top = 0f;
        if (threats is Dictionary<IUnit, float> list)
        {
            foreach (float t in list.Values)
            {
                total += t;
                if (t > top) top = t;
            }
            return;
        }

        foreach (float t in threats.Values)
        {
            total += t;
            if (t > top) top = t;
        }
    }

    private readonly record struct BroadcastState(IUnit Target, DateTime LastSent, float LastTopPercent);
}
