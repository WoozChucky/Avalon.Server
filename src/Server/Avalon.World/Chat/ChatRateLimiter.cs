using Avalon.World.Configuration;
using Microsoft.Extensions.Options;

namespace Avalon.World.Chat;

/// <summary>
/// One budget per sending character for every player chat message (#722): plain chat, party chat (/p) and whispers
/// (/w), and /ignore (#723), which may query the database. Other commands are not counted. A sliding window over <see cref="Window" />, measured on the injected
/// <see cref="TimeProvider" />, with no timers: a queue of send times per character, pruned on each call. The size of
/// the budget is <c>Game:ChatMessagesPerMinute</c>, read on every call; 0 or below turns the limit off.
/// </summary>
/// <remarks>
/// Only a delivered message uses budget, so a caller asks <see cref="Check" /> before it looks anything up or sends
/// anything, and calls <see cref="Record" /> once the message has gone out; a refusal records nothing.
/// <para>
/// Threading: chat runs on the tick (<c>CMSG_CHAT_MESSAGE</c> is handled in the map pass, and the commands run from
/// it), and so does the leave hook. The one caller that may not is the shutdown despawn
/// (<c>WorldServer.OnStoppingAsync</c>), which can overlap a tick that outlives its join and reaches
/// <see cref="Forget" />; every method therefore takes one short lock, which costs nothing on the tick.
/// </para>
/// </remarks>
public sealed class ChatRateLimiter(IOptions<GameConfiguration> options, TimeProvider time)
{
    /// <summary>
    /// The length of the window. A constant, not a setting: the setting is a count "per minute", and a window that
    /// could be changed would make its name wrong.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<uint, Queue<long>> _sent = [];

    /// <summary>The line a sender over the limit is told, with the wait rounded up to whole seconds (at least 1).</summary>
    public static string TooFast(TimeSpan retryAfter) =>
        $"You're sending messages too fast. Try again in {Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))}s.";

    /// <summary>
    /// Whether the character may send a message now. When not, <paramref name="retryAfter" /> is the time until the
    /// oldest counted message leaves the window. Records nothing.
    /// </summary>
    public bool Check(uint characterId, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        int limit = options.Value.ChatMessagesPerMinute;
        if (limit <= 0)
            return true;

        lock (_gate)
        {
            if (!_sent.TryGetValue(characterId, out Queue<long>? queue))
                return true;

            long now = time.GetTimestamp();
            Prune(queue, now);
            if (queue.Count == 0)
                _sent.Remove(characterId);

            if (queue.Count < limit)
                return true;

            retryAfter = Window - time.GetElapsedTime(queue.Peek(), now);
            return false;
        }
    }

    /// <summary>A message from the character was delivered: it uses one place in the window.</summary>
    public void Record(uint characterId)
    {
        if (options.Value.ChatMessagesPerMinute <= 0)
            return;

        lock (_gate)
        {
            long now = time.GetTimestamp();
            if (!_sent.TryGetValue(characterId, out Queue<long>? queue))
            {
                queue = new Queue<long>();
                _sent[characterId] = queue;
            }

            Prune(queue, now);
            queue.Enqueue(now);
        }
    }

    /// <summary>The character left the world: its window is dropped, so nothing is held for a character that is gone.</summary>
    public void Forget(uint characterId)
    {
        lock (_gate)
            _sent.Remove(characterId);
    }

    /// <summary>How many characters hold a window right now (one with nothing counted holds none).</summary>
    public int TrackedCharacters
    {
        get
        {
            lock (_gate)
                return _sent.Count;
        }
    }

    private void Prune(Queue<long> queue, long now)
    {
        while (queue.Count > 0 && time.GetElapsedTime(queue.Peek(), now) >= Window)
            queue.Dequeue();
    }
}
