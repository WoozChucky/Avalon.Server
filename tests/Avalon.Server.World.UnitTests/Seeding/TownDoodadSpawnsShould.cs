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
/// prop with no seed row is a box nothing draws; a row away from its prop is a doodad standing beside its own box.
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
        List<MapDoodadSpawn> rows = [.. context.MapDoodadSpawns.AsNoTracking().AsEnumerable().Where(s => s.MapTemplateId.Value == 1)];
        var models = context.DoodadTemplates.AsNoTracking().AsEnumerable()
            .ToDictionary(t => t.Id.Value, t => t.ModelKey);
        List<MapDoodadSpawn> matched = [];

        foreach (TownSquare square in TownPieces.Squares())
        {
            PlacedChunk chunk = town.Chunks.Single(c => reference.Chunks.GetById(c.TemplateId).Name == square.Name);
            foreach (DecorItem item in square.DoodadDrawn)
            {
                Vector3 world = ChunkRotation.LocalToWorld(item.X, item.Y, item.Z, chunk.Rotation, town.CellSize, chunk.WorldPos);
                MapDoodadSpawn row = Assert.Single(rows, r =>
                    models[r.DoodadTemplateId.Value] == item.Model
                    && Vector3.Distance(town.EntrySpawnWorldPos + new Vector3(r.OffsetX, r.OffsetY, r.OffsetZ), world) < 0.01f);
                Assert.Equal(item.Yaw, row.Yaw);
                matched.Add(row);
            }
        }

        Assert.NotEmpty(matched);
        Assert.Equal(rows.Select(r => r.Id.Value).Order(), matched.Select(r => r.Id.Value).Order());
    }
}
