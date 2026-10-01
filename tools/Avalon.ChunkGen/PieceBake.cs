using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.ChunkGen;

/// <summary>
/// Bakes chunks with the World server's own ChunkLayoutNavmeshBuilder, which reads Maps/Chunks/&lt;name&gt;.obj under the
/// process working directory: the bake stands in <paramref name="contentRoot" /> for its length, as
/// tools/Avalon.Exporter's navmesh vectors do. Throws (NavmeshBuildFailedException) when no navmesh comes out.
/// </summary>
internal static class PieceBake
{
    public static void Check(string contentRoot, IReadOnlyList<(string Name, int GridX, int GridZ)> chunks)
    {
        var placed = chunks.Select((c, i) => new PlacedChunk(
            new ChunkTemplateId(i + 1), (short)c.GridX, (short)c.GridZ, 0,
            new Vector3(c.GridX * ChunkPiece.CellSize, 0f, c.GridZ * ChunkPiece.CellSize))).ToList();
        var layout = new ChunkLayout(0, placed, placed[0], null, [], Vector3.zero, ChunkPiece.CellSize);
        var builder = new ChunkLayoutNavmeshBuilder(NullLoggerFactory.Instance, new NamesLibrary(chunks.Select(c => c.Name).ToList()));

        string previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(contentRoot);
        try
        {
            builder.BuildAsync(layout, CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    /// <summary>The bake only asks a library for each template's name, which is the .obj's file name.</summary>
    private sealed class NamesLibrary(IReadOnlyList<string> names) : IChunkLibrary
    {
        public Task LoadAsync(CancellationToken ct) => Task.CompletedTask;

        public ChunkTemplate GetById(ChunkTemplateId id) =>
            new() { Id = id, Name = names[id.Value - 1], CellSize = ChunkPiece.CellSize };

        public IReadOnlyList<ChunkPoolMember> GetByPool(ChunkPoolId poolId) => [];

        public IReadOnlyList<ChunkGroupDefinition> GetGroupsByPool(ChunkPoolId poolId) => [];

        public IReadOnlyDictionary<ChunkTemplateId, ChunkTemplate> LookupByIds(IEnumerable<ChunkTemplateId> ids) =>
            ids.ToDictionary(id => id, GetById);
    }
}
