using Avalon.ChunkGen;
using Avalon.Database.World.Seeding;

// Writes the generated chunk pieces into the world server's catalog (Maps/Chunks).
//
//   dotnet run --project tools/Avalon.ChunkGen -- forest
//   dotnet run --project tools/Avalon.ChunkGen -- forest --maps <Maps directory>
//
// Every piece is first written into a temporary copy of Maps/, read back through the World server's own catalog
// reader (ChunkCatalogSeeder.ReadCatalogAsync) and baked with its own ChunkLayoutNavmeshBuilder, each set piece with
// its members at their cells; only when all of that passes are the files copied into Maps/Chunks. Committed output is
// checked against a fresh run by ForestPiecesShould, so edit ForestPieces.cs and rerun this rather than editing the
// files. chunk-pools.json and chunk-groups.json are edited by hand.

if (args.Length == 0 || args[0] != "forest")
{
    Console.Error.WriteLine("usage: Avalon.ChunkGen forest [--maps <Maps directory>]");
    return 1;
}

string maps = args.Length >= 3 && args[1] == "--maps"
    ? Path.GetFullPath(args[2])
    : Path.Combine(RepositoryRoot.Find(), "src", "Server", "Avalon.Server.World", "Maps");
IReadOnlyList<(string Name, string Obj, string Json)> files = ChunkFiles.For(ForestPieces.All());

string staging = Path.Combine(Path.GetTempPath(), $"avalon-chunkgen-{Guid.NewGuid():N}");
string stagedMaps = Path.Combine(staging, "Maps");
try
{
    foreach (string file in Directory.EnumerateFiles(maps, "*", SearchOption.AllDirectories))
    {
        string target = Path.Combine(stagedMaps, Path.GetRelativePath(maps, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target);
    }
    foreach ((string name, string obj, string json) in files)
    {
        File.WriteAllText(Path.Combine(stagedMaps, "Chunks", name + ".obj"), obj);
        File.WriteAllText(Path.Combine(stagedMaps, "Chunks", name + ".json"), json);
    }

    await ChunkCatalogSeeder.ReadCatalogAsync(stagedMaps);
    foreach (ChunkPiece piece in ForestPieces.Singles())
        PieceBake.Check(staging, [(piece.Name, 0, 0)]);
    foreach (ChunkGroupPiece group in ForestPieces.Groups())
        PieceBake.Check(staging, group.Members().Select(m => (m.Piece.Name, m.CellX, m.CellZ)).ToList());
}
catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException
                           or System.Text.Json.JsonException or Avalon.World.ChunkLayouts.NavmeshBuildFailedException)
{
    Console.Error.WriteLine($"nothing written: {ex.Message}");
    return 1;
}
finally
{
    if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
}

foreach ((string name, string obj, string json) in files)
{
    File.WriteAllText(Path.Combine(maps, "Chunks", name + ".obj"), obj);
    File.WriteAllText(Path.Combine(maps, "Chunks", name + ".json"), json);
}

Console.WriteLine($"validated, baked and wrote {files.Count} chunks to {Path.Combine(maps, "Chunks")}");
return 0;
