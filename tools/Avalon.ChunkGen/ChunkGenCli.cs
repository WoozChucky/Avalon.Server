using Avalon.Database.World.Seeding;

namespace Avalon.ChunkGen;

/// <summary>
/// The tool's commands (Program.cs runs them with <see cref="ForestPieces" /> and <see cref="TownPieces" />): "forest"
/// writes the forest pieces, "town" Glimmerdell's four squares. Every piece is first written into a temporary copy of
/// Maps/, read back through the World server's own catalog reader (ChunkCatalogSeeder.ReadCatalogAsync) and baked with
/// its own ChunkLayoutNavmeshBuilder, each set piece with its members at their cells and the town with its four squares
/// together; only when all of that passes are the files copied into Maps/Chunks. Any failure before that exits 1 and
/// writes nothing to Maps/.
/// </summary>
public static class ChunkGenCli
{
    public const string Usage = "usage: Avalon.ChunkGen forest|town [--maps <Maps directory>]";

    public static async Task<int> RunAsync(
        string[] args, Func<IReadOnlyList<ChunkPiece>> singles, Func<IReadOnlyList<ChunkGroupPiece>> groups,
        Func<IReadOnlyList<TownSquare>> town, TextWriter output, TextWriter error)
    {
        if (!TryParse(args, out string? command, out string? mapsArgument))
        {
            await error.WriteLineAsync(Usage);
            return 1;
        }

        string staging = Path.Combine(Path.GetTempPath(), $"avalon-chunkgen-{Guid.NewGuid():N}");
        string maps;
        IReadOnlyList<(string Name, string Obj, string Json)> files;
        try
        {
            maps = mapsArgument is null
                ? Path.Combine(RepositoryRoot.Find(), "src", "Server", "Avalon.Server.World", "Maps")
                : Path.GetFullPath(mapsArgument);

            // Each bake: the chunks placed together at their cells (a single piece alone; a set piece's or the town's members together).
            List<List<(string Name, int CellX, int CellZ)>> bakes;
            if (command == "forest")
            {
                IReadOnlyList<ChunkPiece> singlePieces = singles();
                IReadOnlyList<ChunkGroupPiece> groupPieces = groups();
                files = ChunkFiles.For(singlePieces, groupPieces);
                bakes =
                [
                    .. singlePieces.Select(p => new List<(string, int, int)> { (p.Name, 0, 0) }),
                    .. groupPieces.Select(g => g.Members().Select(m => (m.Piece.Name, m.CellX, m.CellZ)).ToList()),
                ];
            }
            else
            {
                IReadOnlyList<TownSquare> squares = town();
                files = ChunkFiles.For(squares);
                bakes = [squares.Select(s => (s.Name, s.GridX, s.GridZ)).ToList()];
            }

            string stagedMaps = Path.Combine(staging, "Maps");
            foreach (string file in Directory.EnumerateFiles(maps, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(stagedMaps, Path.GetRelativePath(maps, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach ((string name, string obj, string json) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(stagedMaps, "Chunks", name + ".obj"), obj);
                await File.WriteAllTextAsync(Path.Combine(stagedMaps, "Chunks", name + ".json"), json);
            }

            await ChunkCatalogSeeder.ReadCatalogAsync(stagedMaps);
            foreach (List<(string Name, int CellX, int CellZ)> bake in bakes)
            {
                if (bake.Count > 0) PieceBake.Check(staging, bake);
            }
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"nothing written: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            DeleteStaging(staging, error);
        }

        string chunks = Path.Combine(maps, "Chunks");
        try
        {
            foreach ((string name, string obj, string json) in files)
            {
                await File.WriteAllTextAsync(Path.Combine(chunks, name + ".obj"), obj);
                await File.WriteAllTextAsync(Path.Combine(chunks, name + ".json"), json);
            }
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"writing {chunks} failed part way, rerun after fixing: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"validated, baked and wrote {files.Count} chunks to {chunks}");

        // Listed, never deleted: a file with the command's prefix that the tool did not write is hand-authored (the four
        // older town chunks, forest_boss_01) or left over from a renamed piece.
        var written = files.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(chunks, command + "_*")
                     .Select(Path.GetFileName).OfType<string>()
                     .Where(f => f.EndsWith(".obj", StringComparison.Ordinal) || f.EndsWith(".json", StringComparison.Ordinal))
                     .Where(f => !written.Contains(Path.GetFileNameWithoutExtension(f)))
                     .Order(StringComparer.Ordinal))
            await output.WriteLineAsync($"not generated by ChunkGen: {file}");

        return 0;
    }

    /// <summary>
    /// Removes the temporary copy of Maps/. A copy that cannot be removed (a file held open in it, say) is only reported:
    /// it runs in a finally, where a throw would replace the run's own exit code and message.
    /// </summary>
    public static void DeleteStaging(string staging, TextWriter error, Action<string>? delete = null)
    {
        try
        {
            if (Directory.Exists(staging)) (delete ?? (path => Directory.Delete(path, recursive: true)))(staging);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"could not remove the temporary copy {staging}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Exactly "forest" or "town", optionally followed by "--maps &lt;dir&gt;"; anything else is refused.</summary>
    private static bool TryParse(string[] args, out string? command, out string? maps)
    {
        command = null;
        maps = null;
        if (args is [("forest" or "town") and var only])
        {
            command = only;
            return true;
        }
        if (args is [("forest" or "town") and var first, "--maps", { Length: > 0 } dir] && !dir.StartsWith("--", StringComparison.Ordinal))
        {
            command = first;
            maps = dir;
            return true;
        }

        return false;
    }
}
