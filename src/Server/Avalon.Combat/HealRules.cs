namespace Avalon.Combat;

/// <summary>The heal rule that is pure arithmetic.</summary>
public static class HealRules
{
    /// <summary>
    /// Health after a heal of <paramref name="amount" />: never past <paramref name="max" />, and health that already
    /// sits at or above the maximum is left as it is, never lowered (#548).
    /// </summary>
    public static uint After(uint before, uint max, uint amount) =>
        before >= max ? before : (uint)Math.Min((ulong)max, (ulong)before + amount);
}
