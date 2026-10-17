namespace Avalon.ChunkGen;

/// <summary>
/// One model the client draws: a kit piece's glTF name without the extension, or an avalon_* custom prop, at a position
/// in metres (chunk-local in a square's decor, anchor-relative in a recipe) and a yaw of 0, 90, 180 or 270 degrees,
/// clockwise seen from above as ChunkRotation turns a chunk. <paramref name="Building" /> is the building the item was
/// placed with (TownBuilding.Place); the decor file does not carry it.
/// </summary>
public sealed record DecorItem(string Building, string Model, float X, float Y, float Z, int Yaw)
{
    public static bool IsYaw(int yaw) => yaw is 0 or 90 or 180 or 270;
}
