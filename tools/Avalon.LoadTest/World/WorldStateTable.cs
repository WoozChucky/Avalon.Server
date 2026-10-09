using Avalon.LoadTest.Wire;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.State;

namespace Avalon.LoadTest.World;

/// <summary>What a bot knows of one object in its view.</summary>
/// <param name="Guid">The object's guid.</param>
/// <param name="Kind">The guid's type byte (<c>guid &gt;&gt; 56</c>): 1 character, 2 creature, 4 projectile, 5 portal, 6 loot.</param>
/// <param name="X">The last position the world sent; 0, 0, 0 until it sends one.</param>
/// <param name="Dead">The last death state the world sent; false until it sends one.</param>
/// <param name="CurrentHealth">The last current health the world sent; 0 until it sends one.</param>
public readonly record struct TrackedObject(ulong Guid, byte Kind, float X, float Y, float Z, bool Dead, int CurrentHealth);

/// <summary>
/// The objects a fighter's character can see, kept as the game client keeps them: by guid from the world-state packets
/// (<c>SMSG_WORLD_STATE_ADD</c>, <c>_UPDATE</c>, <c>_REMOVE</c>), so a fighter can pick the nearest live creature.
/// </summary>
/// <remarks>
/// <para>
/// An add describes an object new to the view, with everything the world has to say about it, and replaces whatever
/// the table held for its guid. An update carries only the members that changed: <see cref="ObjectState" />'s
/// members are nullable, and protobuf-net leaves a member it did not read null, so a null member keeps the value the
/// table already has. A remove drops the guid. The world sends no position for the recipient's own character (its
/// position travels on the acks), so the table never has one for it either.
/// </para>
/// <para>
/// Applied on the connection's read loop and read by the fighter's own task, so every access takes one short lock;
/// the table holds a view's worth of objects (tens), and a lookup walks them all.
/// </para>
/// </remarks>
public sealed class WorldStateTable
{
    /// <summary>The guid's type byte of a creature.</summary>
    public const byte CreatureKind = 2;

    private const int KindShift = 56;

    private readonly Dictionary<ulong, Entry> _objects = new();
    private readonly Lock _lock = new();

    /// <summary>How many objects the table holds.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _objects.Count;
        }
    }

    /// <summary>
    /// Decodes a world-state packet with the connection's codec and applies it; a packet of any other type is ignored.
    /// </summary>
    public void Apply(NetworkPacket packet, PacketCodec codec)
    {
        switch (packet.Header.Type)
        {
            case NetworkPacketType.SMSG_WORLD_STATE_ADD:
                Add(codec.Decode<SInstanceStateAddPacket>(packet).Adds);
                break;
            case NetworkPacketType.SMSG_WORLD_STATE_UPDATE:
                Update(codec.Decode<SInstanceStateUpdatePacket>(packet).Updates);
                break;
            case NetworkPacketType.SMSG_WORLD_STATE_REMOVE:
                Remove(codec.Decode<SInstanceStateRemovePacket>(packet).Removes);
                break;
        }
    }

    /// <summary>Forgets every object, as a new connection or a move to another instance starts with an empty view.</summary>
    public void Clear()
    {
        lock (_lock)
            _objects.Clear();
    }

    /// <summary>What the table knows of <paramref name="guid" />, if it is in view.</summary>
    public bool TryGet(ulong guid, out TrackedObject tracked)
    {
        lock (_lock)
        {
            bool found = _objects.TryGetValue(guid, out Entry entry);
            tracked = entry.Object;
            return found;
        }
    }

    /// <summary>
    /// The nearest creature on the ground plane (X, Z) to (<paramref name="x" />, <paramref name="z" />) that is
    /// alive (not dead, health above 0) and whose position is known, no farther than <paramref name="maxRange" />.
    /// </summary>
    public bool TryNearestLiveCreature(float x, float z, float maxRange, out TrackedObject target)
    {
        target = default;
        float best = maxRange * maxRange;
        bool found = false;
        lock (_lock)
        {
            foreach (Entry entry in _objects.Values)
            {
                TrackedObject candidate = entry.Object;
                if (candidate.Kind != CreatureKind || !entry.Placed || candidate.Dead || candidate.CurrentHealth <= 0)
                    continue;

                float dx = candidate.X - x;
                float dz = candidate.Z - z;
                float distance = dx * dx + dz * dz;
                if (distance > best) continue;

                best = distance;
                target = candidate;
                found = true;
            }
        }

        return found;
    }

    private void Add(List<ObjectState>? states)
    {
        if (states is null) return;

        lock (_lock)
        {
            foreach (ObjectState state in states)
                _objects[state.Guid] = Merge(Unknown(state.Guid), state);
        }
    }

    private void Update(List<ObjectState>? states)
    {
        if (states is null) return;

        lock (_lock)
        {
            foreach (ObjectState state in states)
            {
                // The world updates only what it has added; an update for an unknown guid starts from nothing rather
                // than being lost.
                Entry known = _objects.TryGetValue(state.Guid, out Entry entry) ? entry : Unknown(state.Guid);
                _objects[state.Guid] = Merge(known, state);
            }
        }
    }

    private void Remove(List<ulong>? guids)
    {
        if (guids is null) return;

        lock (_lock)
        {
            foreach (ulong guid in guids)
                _objects.Remove(guid);
        }
    }

    /// <summary><paramref name="known" /> with each member <paramref name="state" /> carries; an absent member keeps its value.</summary>
    private static Entry Merge(Entry known, ObjectState state)
    {
        TrackedObject tracked = known.Object;
        bool placed = known.Placed;
        if (state.Position is { } position)
        {
            tracked = tracked with { X = position.X, Y = position.Y, Z = position.Z };
            placed = true;
        }

        if (state.IsDead is { } dead) tracked = tracked with { Dead = dead };
        if (state.CurrentHealth is { } health)
            tracked = tracked with { CurrentHealth = (int)Math.Min(health, int.MaxValue) };
        return new Entry(tracked, placed);
    }

    /// <summary>An object the table has been told nothing about but its guid.</summary>
    private static Entry Unknown(ulong guid) =>
        new(new TrackedObject(guid, (byte)(guid >> KindShift), 0, 0, 0, false, 0), false);

    /// <param name="Placed">Whether the world has sent a position for the object.</param>
    private readonly record struct Entry(TrackedObject Object, bool Placed);
}
