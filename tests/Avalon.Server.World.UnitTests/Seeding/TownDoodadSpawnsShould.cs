using Avalon.ChunkGen;
using Avalon.Common.Mathematics;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.ChunkLayouts;
using Avalon.World.Testing.Scenarios;
using Microsoft.EntityFrameworkCore;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// The town's doodad-drawn props (TownPieces' DoodadProp: the notice board) keep their collision box in the chunk .obj
/// and leave the decor file, so the client draws them only as the doodads Glimmerdell's MapDoodadSpawns seed places. A
/// prop with no seed row is a box nothing draws; a row away from its prop is a doodad standing beside its own box. The
/// box stays however the doodad is shown, so the doodad must never be hidden: a visibility gate, or a template that
/// despawns, would leave an invisible wall.
/// </summary>
public class TownDoodadSpawnsShould
{
    [Fact]
    public void Draw_every_doodad_drawn_prop_as_a_seeded_town_doodad()
    {
        ScenarioReferenceData reference = ScenarioReferenceData.Shared;
        ChunkLayout town = reference.TownLayout;
        using var database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<MapDoodadSpawn> rows = [.. context.MapDoodadSpawns.AsNoTracking()];

        // Glimmerdell (map 1) is the only map with fixed doodads, so the never-hidden checks below cover every seeded
        // row. A row on another map fails here: extend this guard to that map's props before seeding it.
        Assert.All(rows, r => Assert.Equal(1, r.MapTemplateId.Value));
        var templates = context.DoodadTemplates.AsNoTracking().AsEnumerable().ToDictionary(t => t.Id.Value);
        List<MapDoodadSpawn> matched = [];

        foreach (TownSquare square in TownPieces.Squares())
        {
            PlacedChunk chunk = town.Chunks.Single(c => reference.Chunks.GetById(c.TemplateId).Name == square.Name);
            foreach (DecorItem item in square.DoodadDrawn)
            {
                Vector3 world = ChunkRotation.LocalToWorld(item.X, item.Y, item.Z, chunk.Rotation, town.CellSize, chunk.WorldPos);
                MapDoodadSpawn row = Assert.Single(rows, r =>
                    templates[r.DoodadTemplateId.Value].ModelKey == item.Model
                    && Vector3.Distance(town.EntrySpawnWorldPos + new Vector3(r.OffsetX, r.OffsetY, r.OffsetZ), world) < 0.01f);
                Assert.Equal((item.Yaw + 90 * chunk.Rotation) % 360, row.Yaw);

                // Never hidden: no visibility gate on the row or its template, no use that spends it into a despawn,
                // and no script, which could despawn it on a use or a respawn, or hide it through CanSee.
                DoodadTemplate template = templates[row.DoodadTemplateId.Value];
                Assert.Null(row.VisibleQuestId);
                Assert.Null(template.VisibleQuestId);
                Assert.False(template is { SpentLook: DoodadSpentLook.Despawn, UseLimit: DoodadUseLimit.OncePerInstance },
                    $"doodad {template.Id.Value} despawns once spent");
                Assert.Null(template.ScriptName);
                matched.Add(row);
            }
        }

        Assert.NotEmpty(matched);
        Assert.Equal(rows.Select(r => r.Id.Value).Order(), matched.Select(r => r.Id.Value).Order());
    }
}
