using Avalon.World.ChunkLayouts;

namespace Avalon.ChunkGen;

/// <summary>
/// One scenery piece: what a ring cell around a map draws, never walkable and never a chunk (no catalog row, exits or
/// navmesh). Its terrain is written to <c>Maps/Scenery/&lt;name&gt;.obj</c> (SceneryObjWriter) and its decor, in
/// piece-local metres (0-30 on x and z), to <c>Maps/Decor/&lt;name&gt;.json</c> in <see cref="Kit" />. The forest's
/// pieces carry what the ring file's pool says of them (SceneryJsonWriter): the town's carry their cell instead.
/// </summary>
public sealed record SceneryPiece(string Name, SceneryTerrain Terrain, IReadOnlyList<DecorItem> Decor)
{
    /// <summary>What a forest pool piece is for: a border, a cap or a town-ward piece.</summary>
    public SceneryKind Kind { get; init; }

    /// <summary>
    /// What the piece shows, which the pool rules count: forest borders <c>woods</c>, <c>cliffs</c>, <c>ravine</c>,
    /// <c>giant</c>, <c>stream</c>; single caps <c>rockfall</c>, <c>trunk</c>, <c>thicket</c>, <c>brink</c>; other caps
    /// <c>cap</c>; <c>townward</c>; and the town's <c>town</c>.
    /// </summary>
    public string Family { get; init; } = "";

    /// <summary>A border's or a cap's draw weight.</summary>
    public float Weight { get; init; }

    /// <summary>A border's front: the side the ring generator turns toward the layout.</summary>
    public Side Front { get; init; } = Side.S;

    /// <summary>A cap's capped sides, in its own frame.</summary>
    public IReadOnlyList<Side> Capped { get; init; } = [];

    /// <summary>A border that may stand beside itself.</summary>
    public bool Filler { get; init; }

    /// <summary>A town-ward piece's spot: its offset from the forest's entry chunk.</summary>
    public (int X, int Z)? Spot { get; init; }

    /// <summary>A town ring piece's cell.</summary>
    public int GridX { get; init; }

    /// <summary>A town ring piece's cell.</summary>
    public int GridZ { get; init; }

    /// <summary>The decor file's kit (a DecorJsonWriter kit); an item names its own when it differs.</summary>
    public required string Kit { get; init; }
}
