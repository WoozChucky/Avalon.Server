namespace Avalon.ChunkGen;

/// <summary>Puts a <see cref="BuildingRecipe" /> into a square: its collision proxies and its decor, chunk-local.</summary>
public static class TownBuilding
{
    /// <summary>
    /// The recipe turned about its anchor by <paramref name="rotation" /> quarter turns in ChunkRotation's sense (each
    /// takes a local (x, z) to (z, -x): north to east, clockwise seen from above) and moved to
    /// (<paramref name="anchorX" />, <paramref name="anchorZ" />). Every proxy comes back as
    /// <see cref="Material.Collision" /> under <paramref name="building" />, a box's or gable's extents swapped on an odd
    /// turn and its <see cref="TownPiece.Front" /> turned with it (NegX, PosZ, PosX, NegZ); every piece's yaw grows by
    /// 90 per turn. A gable's ridge runs along its longer extent, so it turns with the extents; a square gable, whose
    /// ridge always runs along X, would not turn and is refused. A rotation outside 0-3 is refused
    /// (ArgumentOutOfRangeException); a piece <see cref="DecorItem.Check" /> refuses, a square gable, or a recipe without
    /// exactly one front (InvalidOperationException).
    /// </summary>
    public static (IReadOnlyList<TownPiece> Proxies, IReadOnlyList<DecorItem> Decor) Place(
        BuildingRecipe recipe, string building, float anchorX, float anchorZ, int rotation)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rotation);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation, 3);

        int fronts = recipe.Proxies.Count(p => p.Front != Facing.None);
        if (fronts != 1)
            throw new InvalidOperationException($"recipe {recipe.Id}: {fronts} proxies carry a front, not exactly one");

        IReadOnlyDictionary<string, ModelBounds> kit = KitBounds.Load();
        List<DecorItem> decor = [];
        foreach (DecorItem piece in recipe.Pieces)
        {
            piece.Check($"recipe {recipe.Id}", kit);
            (float x, float z) = Turn(piece.X, piece.Z, anchorX, anchorZ, rotation);
            decor.Add(piece with { Building = building, X = x, Z = z, Yaw = (piece.Yaw + 90 * rotation) % 360 });
        }

        List<TownPiece> proxies = [];
        foreach (TownPiece proxy in recipe.Proxies)
        {
            if (proxy is GablePiece square && square.MaxX - square.MinX == square.MaxZ - square.MinZ)
            {
                throw new InvalidOperationException(
                    $"recipe {recipe.Id}: {building}/{proxy.Part} is a square gable, whose ridge runs along X whichever way it turns; make one axis longer");
            }
            TownPiece turned;
            switch (proxy)
            {
                case BoxPiece box:
                {
                    (float minX, float maxX, float minZ, float maxZ) = Footprint(box.MinX, box.MaxX, box.MinZ, box.MaxZ, anchorX, anchorZ, rotation);
                    turned = box with { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
                    break;
                }
                case GablePiece gable:
                {
                    (float minX, float maxX, float minZ, float maxZ) = Footprint(gable.MinX, gable.MaxX, gable.MinZ, gable.MaxZ, anchorX, anchorZ, rotation);
                    turned = gable with { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
                    break;
                }
                case CylinderPiece cylinder:
                {
                    (float x, float z) = Turn(cylinder.X, cylinder.Z, anchorX, anchorZ, rotation);
                    turned = cylinder with { X = x, Z = z };
                    break;
                }
                case RingPiece ring:
                {
                    (float x, float z) = Turn(ring.X, ring.Z, anchorX, anchorZ, rotation);
                    turned = ring with { X = x, Z = z };
                    break;
                }
                default:
                    throw new InvalidOperationException($"recipe {recipe.Id}: proxy '{proxy.Part}' is a shape the town does not draw");
            }
            proxies.Add(turned with { Building = building, Material = Material.Collision, Front = TurnFront(proxy.Front, rotation) });
        }

        return (proxies, decor);
    }

    /// <summary>ChunkRotation's case table about the anchor instead of the chunk centre.</summary>
    private static (float X, float Z) Turn(float x, float z, float anchorX, float anchorZ, int rotation) => rotation switch
    {
        0 => (anchorX + x, anchorZ + z),
        1 => (anchorX + z, anchorZ - x),
        2 => (anchorX - x, anchorZ - z),
        _ => (anchorX - z, anchorZ + x),
    };

    private static (float MinX, float MaxX, float MinZ, float MaxZ) Footprint(
        float minX, float maxX, float minZ, float maxZ, float anchorX, float anchorZ, int rotation)
    {
        (float ax, float az) = Turn(minX, minZ, anchorX, anchorZ, rotation);
        (float bx, float bz) = Turn(maxX, maxZ, anchorX, anchorZ, rotation);
        return (Math.Min(ax, bx), Math.Max(ax, bx), Math.Min(az, bz), Math.Max(az, bz));
    }

    private static Facing TurnFront(Facing front, int rotation)
    {
        for (int i = 0; i < rotation; i++)
        {
            front = front switch
            {
                Facing.NegX => Facing.PosZ,
                Facing.PosZ => Facing.PosX,
                Facing.PosX => Facing.NegZ,
                Facing.NegZ => Facing.NegX,
                _ => front,
            };
        }
        return front;
    }
}
