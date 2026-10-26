namespace Avalon.Domain.World;

/// <summary>
/// The fixed number of instability (#1180): each imprint adds a hidden roll to an item's running total, and at
/// <see cref="BrickAt" /> or more the item bricks and is Tainted for good (CK_ItemInstances_Tainted holds the two equal).
/// The total is World-side only: never sent to a client, shown over REST, logged or exported.
/// </summary>
public static class ItemInstability
{
    /// <summary>The total at which an item bricks: Tainted exactly when its instability is at least this.</summary>
    public const int BrickAt = 100;
}
