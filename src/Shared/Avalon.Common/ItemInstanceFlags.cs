namespace Avalon.Common;

/// <summary>
/// State an item instance carries independently of its template. Lives in Avalon.Common rather
/// than Avalon.Domain because Avalon.World.Public must name it and does not reference the domain.
/// </summary>
[Flags]
public enum ItemInstanceFlags
{
    None = 0,
    Attuned = 1,
    Tradeable = 2,
    Broken = 4,
    Cooldown = 8,
}
