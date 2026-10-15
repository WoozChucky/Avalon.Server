namespace Avalon.World.Chat;

/// <summary>
/// A budget per character of so many actions in any <see cref="Window" />: a sliding window measured on the injected
/// <see cref="TimeProvider" />, with no timers, as a queue of times per character pruned on each call. The size is read
/// from <c>limit</c> on every call, so a setting change applies at once; 0 or below turns the budget off. The chat rate
/// limit (<see cref="ChatRateLimiter" />, #722) and the party invite budget (<c>PartyService</c>, #944) are each one.
/// </summary>
/// <remarks>
/// A caller asks <see cref="Check" /> before it does what the budget guards, and calls <see cref="Record" /> once it has
/// done it; a refusal records nothing. Neither allocates once a character's queue exists, and a character with nothing
/// in its window holds no queue. Every method takes one short lock: the callers run on the tick, but the shutdown
/// despawn can reach <see cref="Forget" /> from another thread.
/// </remarks>
internal sealed class CharacterRateBudget(Func<int> limit, TimeProvider time)
{
    /// <summary>
    /// The length of the window. A constant, not a setting: the settings are counts "per minute", and a window that
    /// could be changed would make their names wrong.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<uint, Queue<long>> _done = [];

    /// <summary>
    /// Whether the character may act now. When not, <paramref name="retryAfter" /> is the time until the oldest counted
    /// action leaves the window. Records nothing.
    /// </summary>
    public bool Check(uint characterId, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        int budget = limit();
        if (budget <= 0)
            return true;

        lock (_gate)
        {
            if (!_done.TryGetValue(characterId, out Queue<long>? queue))
                return true;

            long now = time.GetTimestamp();
            Prune(queue, now);
            if (queue.Count == 0)
                _done.Remove(characterId);

            if (queue.Count < budget)
                return true;

            retryAfter = Window - time.GetElapsedTime(queue.Peek(), now);
            return false;
        }
    }

    /// <summary>The character acted: it uses one place in the window.</summary>
    public void Record(uint characterId)
    {
        if (limit() <= 0)
            return;

        lock (_gate)
        {
            long now = time.GetTimestamp();
            if (!_done.TryGetValue(characterId, out Queue<long>? queue))
            {
                queue = new Queue<long>();
                _done[characterId] = queue;
            }

            Prune(queue, now);
            queue.Enqueue(now);
        }
    }

    /// <summary>The character left the world: its window is dropped, so nothing is held for a character that is gone.</summary>
    public void Forget(uint characterId)
    {
        lock (_gate)
            _done.Remove(characterId);
    }

    /// <summary>How many characters hold a window right now (one with nothing counted holds none).</summary>
    public int TrackedCharacters
    {
        get
        {
            lock (_gate)
                return _done.Count;
        }
    }

    private void Prune(Queue<long> queue, long now)
    {
        while (queue.Count > 0 && time.GetElapsedTime(queue.Peek(), now) >= Window)
            queue.Dequeue();
    }
}
