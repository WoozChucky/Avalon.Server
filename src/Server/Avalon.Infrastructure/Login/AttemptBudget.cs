using Avalon.Infrastructure;

namespace Avalon.Infrastructure.Login;

/// <summary>
/// A failed-attempt budget kept as a Redis counter: the mechanics <see cref="SourceBudget"/> and
/// <see cref="UsernameBudget"/> share. Every attempt takes a slot before any work is done, so
/// parallel connections cannot all get past a check made before the first of them is counted; the
/// count the increment returns is the attempt's own place in the window. An attempt that turns out
/// correct gives its own slot back; a failed one keeps it until the window ends.
/// </summary>
public static class AttemptBudget
{
    /// <summary>Takes a slot and returns the count including it. The window starts with the first slot.</summary>
    public static Task<long> TakeAsync(IReplicatedCache cache, string key, TimeSpan window) =>
        cache.IncrementAsync(key, window);

    /// <summary>
    /// Gives back the slot this attempt took, and no more: a success must not clear the failures
    /// others made, or one working account would let an attacker reset the limit between guesses.
    /// </summary>
    public static Task GiveBackAsync(IReplicatedCache cache, string key) => cache.DecrementFloorAsync(key);
}
