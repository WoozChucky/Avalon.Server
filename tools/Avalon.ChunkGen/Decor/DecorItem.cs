namespace Avalon.ChunkGen;

/// <summary>
/// One model the client draws: a kit piece's glTF name without the extension, or an avalon_* custom prop, at a position
/// in metres (chunk-local in a decor file, anchor-relative in a recipe), a yaw in whole degrees, clockwise seen from
/// above as ChunkRotation turns a chunk, and a uniform <paramref name="Scale" /> about the model's origin.
/// <paramref name="Building" /> is the building the item was placed with (TownBuilding.Place); the decor file does
/// not carry it. <paramref name="Kit" /> is the art kit the model is drawn from when it is not the decor file's
/// (null: the file's), so one file may mix kits.
/// </summary>
public sealed record DecorItem(string Building, string Model, float X, float Y, float Z, int Yaw, float Scale = 1f, string? Kit = null)
{
    /// <summary>The kits a decor item may be drawn from; the props kit's models are doodads, never decor.</summary>
    private static readonly string[] s_decorKits = [DecorJsonWriter.TownKit, DecorJsonWriter.ForestKit, DecorJsonWriter.NatureBrightKit];

    /// <summary>The largest scale an item may carry.</summary>
    public const float MaxScale = 4f;

    /// <summary>The smallest scale an item may carry: below it, the file's 3 decimals would write 0.</summary>
    public const float MinScale = 0.0005f;

    /// <summary>
    /// Refuses (InvalidOperationException, prefixed with <paramref name="where" />) a model the kit's bounds table does
    /// not have, a yaw the file's <paramref name="rule" /> does not allow, a coordinate that is not a number, or a
    /// scale that is not a number from <see cref="MinScale" /> (written 0.001) to <see cref="MaxScale" />. The item's
    /// kit (its own, else <paramref name="fileKit" />), when it has one, must be a decor kit (town, nature or bright
    /// nature) that holds the model: the table records each model's kit, and the bright nature kit holds the nature
    /// kit's models but none of the custom avalon_* forest props.
    /// </summary>
    public void Check(string where, IReadOnlyDictionary<string, ModelBounds> kit, DecorYaw rule, string? fileKit = null)
    {
        if (!kit.TryGetValue(Model, out ModelBounds? bounds))
            throw new InvalidOperationException($"{where}: the kit's bounds table has no model '{Model}'");
        if ((Kit ?? fileKit) is { } itemKit)
        {
            if (!s_decorKits.Contains(itemKit))
                throw new InvalidOperationException($"{where}: {Model} names kit '{itemKit}', which is not a decor kit");
            bool inKit = itemKit == DecorJsonWriter.NatureBrightKit
                ? bounds.Kit == DecorJsonWriter.ForestKit && !Model.StartsWith("avalon_", StringComparison.Ordinal)
                : bounds.Kit == itemKit;
            if (!inKit)
                throw new InvalidOperationException($"{where}: {Model} is not a model of {itemKit} (the table has it in {bounds.Kit})");
        }
        if (rule == DecorYaw.QuarterTurns && Yaw is not (0 or 90 or 180 or 270))
            throw new InvalidOperationException($"{where}: {Model} has yaw {Yaw}, not 0, 90, 180 or 270");
        if (rule == DecorYaw.AnyDegree && Yaw is < 0 or > 359)
            throw new InvalidOperationException($"{where}: {Model} has yaw {Yaw}, not 0-359");
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Z))
            throw new InvalidOperationException($"{where}: {Model} stands at ({X}, {Y}, {Z}), not a position");
        if (!(Scale >= MinScale && Scale <= MaxScale))
            throw new InvalidOperationException($"{where}: {Model} has scale {Scale}, not in [0.001, {MaxScale}]");
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
