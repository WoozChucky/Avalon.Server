using Avalon.ChunkGen;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.Server.World.UnitTests.Procedural;
using Avalon.World.Doodads;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// Every seeded solid doodad's collision box against the model the client draws (the kit's bounds table): the box is
/// what the navmesh cuts out, so a box that misses the model lets a player walk through what they see, and one that
/// overreaches stops them short of it. The box also fits the slot a solid doodad stands at.
/// </summary>
public class DoodadCollisionShould
{
    /// <summary>
    /// Models whose footprint is round. A box over a round footprint's full width overreaches it at the corners, so the
    /// box is drawn in by the tolerance on each side: its corners stay near the rim and its sides still within the
    /// tolerance of it.
    /// </summary>
    private static readonly HashSet<string> s_round = new(StringComparer.Ordinal) { "avalon_forest_fountain" };

    [Fact]
    public void Cover_every_solid_doodads_model_with_its_collision_box()
    {
        IReadOnlyDictionary<string, ModelBounds> bounds = KitBounds.Load();
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var solid = context.DoodadTemplates.AsNoTracking().ToList().Where(t => t.Collision == DoodadCollision.Box).ToList();
        Assert.NotEmpty(solid);

        foreach (DoodadTemplate t in solid)
        {
            Assert.True(bounds.TryGetValue(t.ModelKey, out ModelBounds? model), $"doodad {t.Id.Value}: '{t.ModelKey}' is not in the bounds table");
            (float halfX, float halfZ) = (HalfWidth(model!.MinX, model.MaxX), HalfWidth(model.MinZ, model.MaxZ));
            float inset = s_round.Contains(t.ModelKey) ? DecorRules.Tolerance : 0f;

            // The rule: half the footprint (less the inset of a round one) rounded up to 0.05, and the model's height
            // rounded up to 0.05, never below the catalog's least.
            Assert.Equal((UpTo005(halfX - inset), UpTo005(halfZ - inset), UpTo005(Math.Max(DoodadCatalog.MinSolidHeight, model.MaxY))),
                (t.CollisionHalfX, t.CollisionHalfZ, t.CollisionHeight));

            // Its spent look stands in the same box: the box never changes when the doodad is used.
            foreach (string key in new[] { t.ModelKey, t.UsedModelKey }.OfType<string>())
            {
                Assert.True(bounds.TryGetValue(key, out ModelBounds? drawn), $"doodad {t.Id.Value}: '{key}' is not in the bounds table");
                Assert.True(WithinTolerance(t.CollisionHalfX, HalfWidth(drawn!.MinX, drawn.MaxX)) && WithinTolerance(t.CollisionHalfZ, HalfWidth(drawn.MinZ, drawn.MaxZ)),
                    $"doodad {t.Id.Value}: a box {t.CollisionHalfX} x {t.CollisionHalfZ} m is more than {DecorRules.Tolerance} m off '{key}'");
                Assert.True(t.CollisionHeight >= DoodadCatalog.MinSolidHeight && t.CollisionHeight >= drawn.MaxY - DecorRules.Tolerance,
                    $"doodad {t.Id.Value}: a box {t.CollisionHeight} m tall under '{key}', {drawn.MaxY} m tall");
            }

            // A solid doodad stands at a doodad_large slot, at least the edge clearance from its cell's edges, and turned
            // only by quarter turns: a box no wider than that never reaches into the next chunk.
            Assert.True(Math.Max(t.CollisionHalfX, t.CollisionHalfZ) <= ForestPiecesShould.EdgeClearance,
                $"doodad {t.Id.Value}: a box {t.CollisionHalfX} x {t.CollisionHalfZ} m does not fit a slot {ForestPiecesShould.EdgeClearance} m from the edge");
        }
    }

    /// <summary>The model's half width on one axis about its origin, where the doodad stands.</summary>
    private static float HalfWidth(float min, float max) => Math.Max(-min, max);

    /// <summary>Within the decor tolerance, to the table's 3 decimals so float noise never fails a box exactly that far off.</summary>
    private static bool WithinTolerance(float box, float model) => Math.Abs(Math.Round((double)box - model, 3)) <= DecorRules.Tolerance;

    /// <summary>Rounded up to the next 0.05 m, after rounding to the table's 3 decimals so float noise never adds a step.</summary>
    private static float UpTo005(float value) => (float)(Math.Ceiling(Math.Round((decimal)value, 3) * 20m) / 20m);
}
