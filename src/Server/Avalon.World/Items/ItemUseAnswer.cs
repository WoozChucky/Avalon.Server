using Avalon.Network.Packets.Character;

namespace Avalon.World.Items;

/// <summary>A use's answer: the result, the cooldown left (OnCooldown only) and the script's line (Refused only).</summary>
public readonly record struct ItemUseAnswer(ItemUseResult Result, uint CooldownMs = 0, string? Message = null)
{
    public static ItemUseAnswer Of(ItemUseResult result) => new(result);

    /// <summary>Rounded up, so a sub-millisecond remainder never reads as 0 ("ready") on the wire.</summary>
    public static ItemUseAnswer Cooldown(TimeSpan left) =>
        new(ItemUseResult.OnCooldown, (uint)Math.Clamp(Math.Ceiling(left.TotalMilliseconds), 1d, uint.MaxValue));

    public static ItemUseAnswer Refusal(string line) => new(ItemUseResult.Refused, Message: line);
}
