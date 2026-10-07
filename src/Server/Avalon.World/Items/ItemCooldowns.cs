using Avalon.Common.ValueObjects;

namespace Avalon.World.Items;

/// <summary>
/// One character's item cooldowns (item use): until when each item, and each cooldown group, is resting.
/// In memory only and per session: a new CharacterEntity is built at every select, so a logout resets them. Absolute
/// times, so nothing ticks them. Independent of the ability global cooldown. Tick thread only. Groups compare
/// ordinally; a blank group is no group.
/// </summary>
public sealed class ItemCooldowns
{
    private readonly Dictionary<ItemTemplateId, DateTimeOffset> _items = [];
    private readonly Dictionary<string, DateTimeOffset> _groups = new(StringComparer.Ordinal);

    /// <summary>The time left before <paramref name="item" />, of <paramref name="group" />, can be used: the longer of the two.</summary>
    public TimeSpan Remaining(ItemTemplateId item, string? group, DateTimeOffset now)
    {
        TimeSpan left = TimeSpan.Zero;
        if (_items.TryGetValue(item, out DateTimeOffset itemReady) && itemReady > now)
            left = itemReady - now;

        if (!string.IsNullOrWhiteSpace(group) && _groups.TryGetValue(group, out DateTimeOffset groupReady)
            && groupReady - now > left)
        {
            left = groupReady - now;
        }

        return left;
    }

    /// <summary>Rests <paramref name="item" /> and, when it has one, its <paramref name="group" /> for <paramref name="duration" />.</summary>
    public void Start(ItemTemplateId item, string? group, TimeSpan duration, DateTimeOffset now)
    {
        if (duration <= TimeSpan.Zero)
            return;

        _items[item] = now + duration;
        if (!string.IsNullOrWhiteSpace(group))
            _groups[group] = now + duration;
    }
}
