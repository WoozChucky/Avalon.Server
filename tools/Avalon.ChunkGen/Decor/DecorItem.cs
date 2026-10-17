namespace Avalon.ChunkGen;

/// <summary>
/// One model the client draws: a kit piece's glTF name without the extension, or an avalon_* custom prop, at a position
/// in metres (chunk-local in a square's decor, anchor-relative in a recipe) and a yaw of 0, 90, 180 or 270 degrees,
/// clockwise seen from above as ChunkRotation turns a chunk. <paramref name="Building" /> is the building the item was
/// placed with (TownBuilding.Place); the decor file does not carry it.
/// </summary>
public sealed record DecorItem(string Building, string Model, float X, float Y, float Z, int Yaw)
{
    /// <summary>
    /// Refuses (InvalidOperationException, prefixed with <paramref name="where" />) a model the kit's bounds table does
    /// not have, a yaw other than 0, 90, 180 or 270, or a coordinate that is not a number.
    /// </summary>
    public void Check(string where, IReadOnlyDictionary<string, ModelBounds> kit)
    {
        if (!kit.ContainsKey(Model))
            throw new InvalidOperationException($"{where}: the kit's bounds table has no model '{Model}'");
        if (Yaw is not (0 or 90 or 180 or 270))
            throw new InvalidOperationException($"{where}: {Model} has yaw {Yaw}, not 0, 90, 180 or 270");
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z))
            throw new InvalidOperationException($"{where}: {Model} stands at ({X}, {Y}, {Z}), not a position");
    }
}
