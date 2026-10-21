using Avalon.Domain.World;

namespace Avalon.World.Auras;

/// <summary>
/// A character's PvP diminishing returns (#770): for each of stun, root and silence, how many applications from other
/// characters landed on it, and when the last did. The count starts over once no application of that type has landed
/// for the window (<c>Game:CrowdControlDiminishWindow</c>); an application refused at the immune step is not recorded,
/// so it never restarts the window. In memory only, never saved: a new session starts clean. One fixed slot per type,
/// so asking and recording allocate nothing. Tick thread only. World-side.
/// </summary>
public sealed class ControlDiminishing
{
    private Slot _stun;
    private Slot _root;
    private Slot _silence;

    /// <summary>
    /// How many applications of <paramref name="type" /> (one of stun, root or silence) already landed within the
    /// window of the last one; 0, and the count started over, once <paramref name="window" /> has passed since it.
    /// </summary>
    public int Landed(CrowdControl type, DateTimeOffset now, TimeSpan window)
    {
        ref Slot slot = ref SlotOf(type);
        if (slot.Count > 0 && now - slot.LastLanded >= window)
            slot = default;

        return slot.Count;
    }

    /// <summary>An application of <paramref name="type" /> landed now: one more step, and the window runs from now.</summary>
    public void Record(CrowdControl type, DateTimeOffset now)
    {
        ref Slot slot = ref SlotOf(type);
        slot.Count++;
        slot.LastLanded = now;
    }

    private ref Slot SlotOf(CrowdControl type)
    {
        switch (type)
        {
            case CrowdControl.Stun:
                return ref _stun;
            case CrowdControl.Root:
                return ref _root;
            case CrowdControl.Silence:
                return ref _silence;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "One of stun, root or silence");
        }
    }

    private struct Slot
    {
        public int Count;
        public DateTimeOffset LastLanded;
    }
}
