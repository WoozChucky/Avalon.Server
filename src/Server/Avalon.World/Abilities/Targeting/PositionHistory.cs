using Avalon.Common.Mathematics;

namespace Avalon.World.Abilities.Targeting;

/// <summary>
/// A unit's recent positions by world tick (<c>WorldTicks.Completed</c>), for lag compensation (#1082): where the unit
/// stood at the tick a client saw when it aimed, so a hit can be judged against what the client was shown. Each entry is
/// the unit's position at the end of its tick, the same state the broadcast stamped with that tick carries. Tick thread
/// only.
/// </summary>
/// <remarks>
/// <para>
/// The instance seeds the history as the unit joins and records it on each tick the unit moved (<c>MapInstance</c>, step
/// 4b, over the moves its spatial grid listed), and an entry is written only when the position changed, so a unit
/// standing still keeps its older entries and the lookup is "the newest entry at or before the tick".
/// <see cref="Capacity" /> entries cover the rewind cap of 15 ticks with room to spare: a unit that moves every tick
/// writes one entry a tick, and one that moves less often reaches further back.
/// </para>
/// <para>
/// One preallocated ring, written in place: a record or a lookup allocates nothing (#880). The instance rents one per
/// member from its pool as the unit joins and returns it as the unit leaves, so a unit moving between instances starts
/// with an empty history; a jump inside an instance (a creature's teleport, a revive) calls <see cref="Invalidate" />, so a
/// rewind never returns a position from before the jump.
/// </para>
/// <para>
/// Public rather than internal because Avalon.World is strong-named and the test assembly is not, so an
/// InternalsVisibleTo cannot name it; Avalon.World is not the modding API.
/// </para>
/// </remarks>
public sealed class PositionHistory
{
    /// <summary>The entries kept: the 15-tick rewind cap and a margin (#1082).</summary>
    public const int Capacity = 18;

    private readonly (long Tick, Vector3 Position)[] _entries = new (long Tick, Vector3 Position)[Capacity];

    // How many entries hold a recorded position, at most Capacity; 0 when empty or invalidated.
    private int _count;

    // The index of the newest entry, meaningful while _count > 0.
    private int _newest;

    /// <summary>Empties the history, as the instance rents it out: the next <see cref="Record" /> seeds it.</summary>
    public void Clear() => _count = 0;

    /// <summary>
    /// A jump (a teleport, a revive where the unit died): every entry from before it is forgotten, and the next
    /// <see cref="Record" /> resets the history to that one entry, even at a position equal to the last one recorded.
    /// </summary>
    public void Invalidate() => Clear();

    /// <summary>
    /// Records the unit at <paramref name="position" /> on <paramref name="tick" />. A position equal to the newest entry's
    /// is skipped, so that entry stands for the ticks since; a second record on the same tick overwrites that tick's entry,
    /// so it holds the unit's last position of the tick.
    /// </summary>
    public void Record(long tick, Vector3 position)
    {
        if (_count > 0)
        {
            ref (long Tick, Vector3 Position) newest = ref _entries[_newest];

            // Exact equality, as CharacterEntity.Position compares: the operator is approximate.
            if (newest.Position.Equals(position))
                return;

            if (newest.Tick == tick)
            {
                newest.Position = position;
                return;
            }

            _newest = (_newest + 1) % Capacity;
        }
        else
        {
            _newest = 0;
        }

        _entries[_newest] = (tick, position);
        if (_count < Capacity)
            _count++;
    }

    /// <summary>
    /// The newest entry at or before <paramref name="tick" />: where the unit stood at the end of that tick. False when
    /// there is none (the unit joined, or jumped, after it, or the tick is older than the ring reaches), in which case a
    /// rewound query uses the unit's present position.
    /// </summary>
    public bool TryGet(long tick, out Vector3 position)
    {
        int index = _newest;
        for (int i = 0; i < _count; i++)
        {
            ref readonly (long Tick, Vector3 Position) entry = ref _entries[index];
            if (entry.Tick <= tick)
            {
                position = entry.Position;
                return true;
            }

            index = index == 0 ? Capacity - 1 : index - 1;
        }

        position = default;
        return false;
    }
}
