namespace Avalon.World.Items;

/// <summary>A restore script's amount: floor(maximum × percent / 100); no percent is nothing.</summary>
public static class ItemUsePercent
{
    public static uint Of(uint maximum, uint? percent) => (uint)((ulong)maximum * (percent ?? 0u) / 100u);
}
