using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Doodads;
using Avalon.World.Testing;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>
/// A view decision asks the doodad index for the doodads within reach of a character, so the index must find exactly the
/// ones a test of every doodad finds: one missed is a doodad a player never sees, one found twice a duplicate add.
/// </summary>
public class DoodadIndexShould
{
    private const float Cell = 30f;

    [Fact]
    public void Visit_exactly_the_doodads_within_reach()
    {
        var random = new Random(1_017);
        var index = DoodadIndex.For(Layout(4, 4));
        List<Doodad> doodads = [];
        for (uint id = 1; id <= 200; id++)
        {
            // Over the layout and up to 20 m past each edge, so the clamped edge cells are exercised too.
            var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, id), Template(),
                new Vector3(Coordinate(random), 0f, Coordinate(random)), (float)(random.NextDouble() * 360));
            doodads.Add(doodad);
            index.Add(doodad);
        }

        for (int query = 0; query < 50; query++)
        {
            float x = Coordinate(random);
            float z = Coordinate(random);
            float reach = (float)(random.NextDouble() * 80);

            var collector = new Collector([]);
            index.Visit(x, z, reach, ref collector);

            var expected = doodads.Where(d => Within(d, x, z, reach)).Select(d => d.Guid.RawValue).Order().ToList();
            var visited = collector.Visited.Select(d => d.Guid.RawValue).Order().ToList();
            Assert.Equal(expected, visited);
        }

        // A view decision runs this for every client on every broadcast tick: it must allocate nothing.
        var counter = new Counter();
        index.Visit(60f, 60f, 70f, ref counter);
        long before = ThreadAllocations.BeforeQuietWindow();
        for (int i = 0; i < 100; i++)
            index.Visit(60f, 60f, 70f, ref counter);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(counter.Count > 0);
    }

    private static bool Within(Doodad doodad, float x, float z, float reach)
    {
        float dx = doodad.Position.x - x;
        float dz = doodad.Position.z - z;
        return dx * dx + dz * dz <= reach * reach;
    }

    private static float Coordinate(Random random) => (float)(random.NextDouble() * (4 * Cell + 40) - 20);

    private static ChunkLayout Layout(int columns, int rows)
    {
        List<PlacedChunk> chunks = [];
        for (short gx = 0; gx < columns; gx++)
        {
            for (short gz = 0; gz < rows; gz++)
                chunks.Add(new PlacedChunk(new ChunkTemplateId(1), gx, gz, 0, new Vector3(gx * Cell, 0f, gz * Cell)));
        }

        return new ChunkLayout(0, chunks, chunks[0], null, [], Vector3.zero, Cell);
    }

    private static DoodadTemplateView Template() => new(
        new DoodadTemplateId(1), "Barrel", "props-kit", "Barrel", null, null, null, null,
        DoodadInteraction.Click, 0, 5f, 0f, DoodadUseLimit.OncePerInstance, DoodadSpentLook.ShowUsed, null, null,
        null, 0, 0, 0, DoodadCollision.None, 0f, 0f, 0f, null, null, [], 0, ScriptCanSee: false);

    private readonly record struct Collector(List<Doodad> Visited) : IDoodadVisitor
    {
        public void Visit(Doodad doodad) => Visited.Add(doodad);
    }

    private struct Counter : IDoodadVisitor
    {
        public int Count;

        public void Visit(Doodad doodad) => Count++;
    }
}
