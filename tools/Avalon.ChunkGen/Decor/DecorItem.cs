namespace Avalon.ChunkGen;

/// <summary>
/// One model the client draws: a kit piece's glTF name without the extension, or an avalon_* custom prop, at a position
/// in metres (chunk-local in a decor file, anchor-relative in a recipe), a yaw in whole degrees, clockwise seen from
/// above as ChunkRotation turns a chunk, and a uniform <paramref name="Scale" /> about the model's origin.
/// <paramref name="Building" /> is the building the item was placed with (TownBuilding.Place); the decor file does
/// not carry it.
/// </summary>
public sealed record DecorItem(string Building, string Model, float X, float Y, float Z, int Yaw, float Scale = 1f)
{
    /// <summary>The largest scale an item may carry.</summary>
    public const float MaxScale = 4f;

    /// <summary>
    /// Refuses (InvalidOperationException, prefixed with <paramref name="where" />) a model the kit's bounds table does
    /// not have, a yaw the file's <paramref name="rule" /> does not allow, a coordinate that is not a number, or a
    /// scale that is not a number in (0, <see cref="MaxScale" />].
    /// </summary>
    public void Check(string where, IReadOnlyDictionary<string, ModelBounds> kit, DecorYaw rule)
    {
        if (!kit.ContainsKey(Model))
            throw new InvalidOperationException($"{where}: the kit's bounds table has no model '{Model}'");
        if (rule == DecorYaw.QuarterTurns && Yaw is not (0 or 90 or 180 or 270))
            throw new InvalidOperationException($"{where}: {Model} has yaw {Yaw}, not 0, 90, 180 or 270");
        if (rule == DecorYaw.AnyDegree && Yaw is < 0 or > 359)
            throw new InvalidOperationException($"{where}: {Model} has yaw {Yaw}, not 0-359");
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z))
            throw new InvalidOperationException($"{where}: {Model} stands at ({X}, {Y}, {Z}), not a position");
        if (!(Scale > 0f && Scale <= MaxScale))
            throw new InvalidOperationException($"{where}: {Model} has scale {Scale}, not in (0, {MaxScale}]");
    }
}

/// <summary>
/// The yaws a decor file allows: a town square's quarter turns, or any whole degree in a forest piece's.
/// </summary>
public enum DecorYaw
{
    /// <summary>0, 90, 180 or 270: the town kit's pieces meet on a grid.</summary>
    QuarterTurns,

    /// <summary>0-359: trees, rocks and ground cover face any way.</summary>
    AnyDegree,
}
