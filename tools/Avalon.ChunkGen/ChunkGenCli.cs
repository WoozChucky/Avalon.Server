using System.Globalization;
using Avalon.Database.World.Seeding;
using Avalon.World.ChunkLayouts;

namespace Avalon.ChunkGen;

/// <summary>
/// The tool's commands (Program.cs runs them with <see cref="ForestPieces" />, <see cref="TownPieces" />,
/// <see cref="ForestScenery" /> and <see cref="TownScenery" />): "forest" writes the forest pieces and the forest's
/// scenery, "town" Glimmerdell's four squares and the town's scenery ring. Every piece is first written into a temporary
/// copy of Maps/, read back through the World server's own catalog reader (ChunkCatalogSeeder.ReadCatalogAsync) and
/// baked with its own ChunkLayoutNavmeshBuilder, each set piece with its members at their cells and the town with its
/// four squares together; the map's scenery pieces are checked (ChunkFiles.ForestScenery or ChunkFiles.TownScenery),
/// their names against the staged catalog's chunks (SceneryRules.CheckNames), and the ring file read back through the
/// World server's scenery reader (SceneryCatalog.Load). Only when all of that passes are the
/// files copied into Maps/Chunks, each town square's and forest piece's decor file into Maps/Decor (which the seeder
/// never reads), and each scenery piece's terrain and the ring file into Maps/Scenery, its decor into Maps/Decor. Any
/// failure before that exits 1 and writes nothing to Maps/.
/// "kit-bounds" measures the art kit's glTF files into Kit/kit-bounds.json (KitBounds), and copies the custom forest
/// models' footprints into Kit/forest-footprints.json (ForestFootprints).
/// </summary>
public static class ChunkGenCli
{
    public const string Usage =
        "usage: Avalon.ChunkGen forest|town [--maps <Maps directory>] | kit-bounds --kit <glTF directory> [--props <glTF directory>] [--props-kit <glTF directory>] [--nature-kit <glTF directory>] [--forest-props <glTF directory>]";

    /// <summary>
    /// The kit-bounds directories, in the order they are read, each with the kit its models are recorded in: the art kit
    /// (required) and the custom town props in the village kit, the props kit, then the nature kit and the custom forest
    /// models in the nature kit.
    /// </summary>
    private static readonly (string Option, string Kit)[] s_kitDirectories =
    [
        ("--kit", DecorJsonWriter.TownKit), ("--props", DecorJsonWriter.TownKit), ("--props-kit", DecorJsonWriter.PropsKit),
        ("--nature-kit", DecorJsonWriter.ForestKit), ("--forest-props", DecorJsonWriter.ForestKit),
    ];

    public static async Task<int> RunAsync(
        string[] args, Func<IReadOnlyList<ChunkPiece>> singles, Func<IReadOnlyList<ChunkGroupPiece>> groups,
        Func<IReadOnlyList<TownSquare>> town, Func<IReadOnlyList<SceneryPiece>> forestScenery, Func<IReadOnlyList<SceneryPiece>> townScenery,
        TextWriter output, TextWriter error)
    {
        if (!TryParse(args, out string? command, out IReadOnlyDictionary<string, string> options))
        {
            await error.WriteLineAsync(Usage);
            return 1;
        }
        if (command == "kit-bounds") return await WriteKitBoundsAsync(options, output, error);

        string? mapsArgument = options.GetValueOrDefault("--maps");

        string staging = Path.Combine(Path.GetTempPath(), $"avalon-chunkgen-{Guid.NewGuid():N}");
        string maps;
        IReadOnlyList<(string Name, string Obj, string Json, string? Decor)> files;
        SceneryFiles? scenery = null;
        try
        {
            maps = mapsArgument is null
                ? Path.Combine(RepositoryRoot.Find("Pass --maps <dir> instead."), "src", "Server", "Avalon.Server.World", "Maps")
                : Path.GetFullPath(mapsArgument);

            // Each bake: the chunks placed together at their cells (a single piece alone; a set piece's or the town's members together).
            List<List<(string Name, int CellX, int CellZ)>> bakes;
            if (command == "forest")
            {
                IReadOnlyList<ChunkPiece> singlePieces = singles();
                IReadOnlyList<ChunkGroupPiece> groupPieces = groups();
                files = ChunkFiles.For(singlePieces, groupPieces);
                scenery = ChunkFiles.ForestScenery(forestScenery());
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
                scenery = ChunkFiles.TownScenery(townScenery());
                bakes = [squares.Select(s => (s.Name, s.GridX, s.GridZ)).ToList()];
            }

            string stagedMaps = Path.Combine(staging, "Maps");
            foreach (string file in Directory.EnumerateFiles(maps, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(stagedMaps, Path.GetRelativePath(maps, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            await WriteAsync(stagedMaps, files);

            ChunkCatalogFiles catalog = await ChunkCatalogSeeder.ReadCatalogAsync(stagedMaps);
            if (scenery is not null)
            {
                // A scenery piece's decor file shares Maps/Decor with the chunks': no name may be both.
                SceneryRules.CheckNames(scenery.Pieces.Select(p => p.Name), catalog.Chunks.Select(c => c.Name));
                await WriteAsync(stagedMaps, scenery);
                SceneryCatalog.Load(stagedMaps);
            }
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
            await WriteAsync(maps, files);
            if (scenery is not null) await WriteAsync(maps, scenery);
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"writing {maps} failed part way, rerun after fixing: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"validated, baked and wrote {files.Count} chunks to {chunks}");
        int decor = files.Count(f => f.Decor is not null);
        if (decor > 0) await output.WriteLineAsync($"wrote {decor} decor files to {Path.Combine(maps, "Decor")}");
        if (scenery is not null)
        {
            await output.WriteLineAsync(
                $"wrote {scenery.Pieces.Count} scenery pieces to {Path.Combine(maps, "Scenery")} (their decor to {Path.Combine(maps, "Decor")}) "
                + $"and the ring file {Path.Combine(maps, "Scenery", RingFile(scenery))}");
        }

        // Listed, never deleted: a file with the command's prefix that the tool did not write is hand-authored (the four
        // older town chunks, forest_boss_01) or left over from a renamed piece, in Maps/Chunks, Maps/Decor and
        // Maps/Scenery.
        var written = files.Select(f => f.Name).Concat(scenery?.Pieces.Select(p => p.Name) ?? []).ToHashSet(StringComparer.Ordinal);
        foreach (string file in NotWritten(chunks, command!, written, ".obj", ".json"))
            await output.WriteLineAsync($"not generated by ChunkGen: {file}");
        string decorDirectory = Path.Combine(maps, "Decor");
        if (Directory.Exists(decorDirectory))
        {
            foreach (string file in NotWritten(decorDirectory, command!, written, ".json"))
                await output.WriteLineAsync($"not generated by ChunkGen: Decor/{file}");
        }
        string sceneryDirectory = Path.Combine(maps, "Scenery");
        if (Directory.Exists(sceneryDirectory))
        {
            foreach (string file in NotWritten(sceneryDirectory, command!, written, ".obj"))
                await output.WriteLineAsync($"not generated by ChunkGen: Scenery/{file}");
        }

        return 0;
    }

    /// <summary>The file names in <paramref name="directory" /> with the command's prefix and one of the extensions that the run did not write.</summary>
    private static IEnumerable<string> NotWritten(string directory, string command, HashSet<string> written, params string[] extensions) =>
        Directory.EnumerateFiles(directory, command + "_*")
            .Select(Path.GetFileName).OfType<string>()
            .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.Ordinal)))
            .Where(f => !written.Contains(Path.GetFileNameWithoutExtension(f)))
            .Order(StringComparer.Ordinal);

    /// <summary>Each chunk's .obj and .json into Maps/Chunks, and its decor file, when it has one, into Maps/Decor.</summary>
    private static async Task WriteAsync(string maps, IReadOnlyList<(string Name, string Obj, string Json, string? Decor)> files)
    {
        foreach ((string name, string obj, string json, string? decor) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(maps, "Chunks", name + ".obj"), obj);
            await File.WriteAllTextAsync(Path.Combine(maps, "Chunks", name + ".json"), json);
            if (decor is null) continue;
            Directory.CreateDirectory(Path.Combine(maps, "Decor"));
            await File.WriteAllTextAsync(Path.Combine(maps, "Decor", name + ".json"), decor);
        }
    }

    /// <summary>
    /// Each scenery piece's terrain into Maps/Scenery/&lt;name&gt;.obj and its decor into Maps/Decor/&lt;name&gt;.json, and
    /// the map's ring file into Maps/Scenery/&lt;map id&gt;.json.
    /// </summary>
    private static async Task WriteAsync(string maps, SceneryFiles scenery)
    {
        Directory.CreateDirectory(Path.Combine(maps, "Scenery"));
        Directory.CreateDirectory(Path.Combine(maps, "Decor"));
        foreach ((string name, string obj, string decor) in scenery.Pieces)
        {
            await File.WriteAllTextAsync(Path.Combine(maps, "Scenery", name + ".obj"), obj);
            await File.WriteAllTextAsync(Path.Combine(maps, "Decor", name + ".json"), decor);
        }
        await File.WriteAllTextAsync(Path.Combine(maps, "Scenery", RingFile(scenery)), scenery.Ring);
    }

    /// <summary>The map's ring file name: &lt;map id&gt;.json.</summary>
    private static string RingFile(SceneryFiles scenery) => scenery.MapId.ToString(CultureInfo.InvariantCulture) + ".json";

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

    /// <summary>
    /// Measures every *.gltf directly in the kit directory and in each optional one (the custom props, the props kit, the
    /// nature kit, the custom forest models), and rewrites Kit/kit-bounds.json, each model under its directory's kit
    /// (<see cref="s_kitDirectories" />); with the custom forest models, also
    /// copies their footprints.json into Kit/forest-footprints.json. A name found in two of them, an empty directory, a
    /// file that cannot be measured or a footprints file that does not match its models exits 1 and leaves both tables as
    /// they were.
    /// </summary>
    private static async Task<int> WriteKitBoundsAsync(IReadOnlyDictionary<string, string> options, TextWriter output, TextWriter error)
    {
        string path;
        string json;
        string? footprints;
        int count;
        try
        {
            path = Path.Combine(
                RepositoryRoot.Find("kit-bounds rewrites the repository's tools/Avalon.ChunkGen/Kit/kit-bounds.json: run it from a build inside the repository."),
                "tools", "Avalon.ChunkGen", "Kit", "kit-bounds.json");
            var table = new Dictionary<string, ModelBounds>(StringComparer.Ordinal);
            foreach ((string option, string kit) in s_kitDirectories)
            {
                if (!options.TryGetValue(option, out string? directory)) continue;
                string[] models = [.. Directory.EnumerateFiles(Path.GetFullPath(directory), "*.gltf").Order(StringComparer.Ordinal)];
                if (models.Length == 0) throw new InvalidOperationException($"no .gltf files in {directory}");
                foreach (string model in models)
                {
                    string name = Path.GetFileNameWithoutExtension(model);
                    if (!table.TryAdd(name, KitBounds.Measure(model, kit))) throw new InvalidOperationException($"{name} is in two directories");
                }
            }
            json = KitBounds.Write(table);
            count = table.Count;
            footprints = options.TryGetValue("--forest-props", out string? forestProps) ? FootprintsOf(forestProps) : null;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"nothing written: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        string footprintsPath = Path.Combine(Path.GetDirectoryName(path)!, "forest-footprints.json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            if (footprints is not null) await File.WriteAllTextAsync(footprintsPath, footprints);
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"writing {path} failed, rerun after fixing: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        await output.WriteLineAsync($"measured {count} models into {path}");
        if (footprints is not null) await output.WriteLineAsync($"copied the forest props' footprints into {footprintsPath}");
        return 0;
    }

    /// <summary>
    /// The custom forest models' footprints (ForestFootprints), read from the art's footprints.json beside their glTF
    /// files and written as committed. A file missing, a model without a footprint or a footprint without a model is refused.
    /// </summary>
    private static string FootprintsOf(string directory)
    {
        string file = Path.Combine(Path.GetFullPath(directory), ForestFootprints.ArtFile);
        if (!File.Exists(file)) throw new InvalidOperationException($"no {ForestFootprints.ArtFile} in {directory}");
        IReadOnlyDictionary<string, IReadOnlyList<FootprintPart>> footprints = ForestFootprints.Read(File.ReadAllText(file));
        string[] models = [.. Directory.EnumerateFiles(Path.GetFullPath(directory), "*.gltf").Select(Path.GetFileNameWithoutExtension).OfType<string>()];
        string[] unmatched = [.. models.Except(footprints.Keys).Concat(footprints.Keys.Except(models)).Order(StringComparer.Ordinal)];
        if (unmatched.Length > 0)
            throw new InvalidOperationException($"{file} and the models beside it disagree on: {string.Join(", ", unmatched)}");
        return ForestFootprints.Write(footprints);
    }

    /// <summary>
    /// "forest" or "town", optionally followed by "--maps &lt;dir&gt;", or "kit-bounds --kit &lt;dir&gt;" optionally with
    /// "--props", "--props-kit", "--nature-kit" and "--forest-props" (each a &lt;dir&gt;), the options in any order and
    /// each at most once; anything else is refused.
    /// </summary>
    private static bool TryParse(string[] args, out string? command, out IReadOnlyDictionary<string, string> options)
    {
        command = null;
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        options = parsed;
        string[] allowed = args switch
        {
            [("forest" or "town"), ..] => ["--maps"],
            ["kit-bounds", ..] => [.. s_kitDirectories.Select(d => d.Option)],
            _ => [],
        };
        if (allowed.Length == 0 || args.Length % 2 == 0) return false;
        for (int i = 1; i < args.Length; i += 2)
        {
            string value = args[i + 1];
            if (!allowed.Contains(args[i]) || value.Length == 0 || value.StartsWith("--", StringComparison.Ordinal) || !parsed.TryAdd(args[i], value))
                return false;
        }
        if (args[0] == "kit-bounds" && !parsed.ContainsKey("--kit")) return false;

        command = args[0];
        return true;
    }
}
