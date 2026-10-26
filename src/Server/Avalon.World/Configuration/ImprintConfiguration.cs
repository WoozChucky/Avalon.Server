namespace Avalon.World.Configuration;

/// <summary>
/// Game:Items:Imprint (#1180 part C): the bounds of the hidden instability each imprint at the Resonance Workbench adds to
/// its item, one whole-percent draw uniform within them, both ends included. An item whose total reaches
/// <see cref="Avalon.Domain.World.ItemInstability.BrickAt" /> bricks. Checked with the rest of Game:Items
/// (<see cref="ItemConfiguration.Problem" />), so a value out of range stops the start.
/// </summary>
public sealed class ImprintConfiguration
{
    /// <summary>Game:Items:Imprint:InstabilityMinPct: the least one imprint adds, 0-100 and at most the maximum.</summary>
    public int InstabilityMinPct { get; set; } = 20;

    /// <summary>Game:Items:Imprint:InstabilityMaxPct: the most one imprint adds, 0-100 and at least the minimum.</summary>
    public int InstabilityMaxPct { get; set; } = 45;

    /// <summary>Null when the bounds satisfy 0 &lt;= min &lt;= max &lt;= 100, else a message naming both.</summary>
    internal string? Problem() =>
        InstabilityMinPct >= 0 && InstabilityMinPct <= InstabilityMaxPct && InstabilityMaxPct <= 100
            ? null
            : "Game:Items:Imprint:InstabilityMinPct and InstabilityMaxPct must satisfy 0 <= min <= max <= 100 " +
              $"(was {InstabilityMinPct} and {InstabilityMaxPct})";
}
