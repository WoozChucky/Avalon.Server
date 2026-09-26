using Avalon.Configuration;
using Avalon.Database.World;
using Avalon.Database.World.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Copies an editor export into the committed catalog under Maps/, then seeds the dev database from
// it with the same seeder the World server runs on start. Commit the files it writes: they are the
// source of truth for every environment.
//   <exportDir>/<chunk>/chunk.json + chunk.obj  ->  Maps/Chunks/<chunk>.json + <chunk>.obj
//   <exportDir>/town_layouts/*.json             ->  Maps/TownLayouts/*.json
// Pool membership is edited by hand in Maps/chunk-pools.json.

var exportDir = args.Length > 0 ? args[0] : "chunks-export";
const string mapsRoot = "src/Server/Avalon.Server.World/Maps";
var chunksOutDir = Path.Combine(mapsRoot, "Chunks");
var layoutsOutDir = Path.Combine(mapsRoot, "TownLayouts");

if (!Directory.Exists(exportDir))
{
    Console.WriteLine($"Export directory '{exportDir}' does not exist. Nothing to import.");
    return 0;
}

Directory.CreateDirectory(chunksOutDir);
int copied = 0;
foreach (var dir in Directory.EnumerateDirectories(exportDir))
{
    var name = Path.GetFileName(dir);
    var jsonPath = Path.Combine(dir, "chunk.json");
    var objPath = Path.Combine(dir, "chunk.obj");
    if (!File.Exists(jsonPath) || !File.Exists(objPath))
    {
        if (name != "town_layouts") Console.WriteLine($"Skipping '{dir}': missing chunk.json or chunk.obj");
        continue;
    }

    File.Copy(jsonPath, Path.Combine(chunksOutDir, $"{name}.json"), overwrite: true);
    File.Copy(objPath, Path.Combine(chunksOutDir, $"{name}.obj"), overwrite: true);
    copied++;
}
Console.WriteLine($"Copied {copied} chunks into {chunksOutDir}.");

var townLayoutsDir = Path.Combine(exportDir, "town_layouts");
if (Directory.Exists(townLayoutsDir))
{
    Directory.CreateDirectory(layoutsOutDir);
    foreach (var jsonFile in Directory.EnumerateFiles(townLayoutsDir, "*.json"))
        File.Copy(jsonFile, Path.Combine(layoutsOutDir, Path.GetFileName(jsonFile)), overwrite: true);
}

await using var ctx = BuildDbContext();
ChunkCatalogSeedResult result = await ChunkCatalogSeeder.SeedAsync(ctx, mapsRoot);
Console.WriteLine(
    $"Seeded the dev database: {result.TemplatesAdded} new, {result.TemplatesUpdated} updated chunks, " +
    $"{result.LayoutsReplaced} town layout(s), {result.PoolsSynced} pool(s).");
return 0;

static WorldDbContext BuildDbContext()
{
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.Design.json", optional: true)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    var dbConfig = new DatabaseConfiguration();
    config.GetSection("Database").Bind(dbConfig);

    var conn = dbConfig.World?.ConnectionString
        ?? config["Database:World:ConnectionString"]
        ?? throw new InvalidOperationException(
            "World connection string not configured. Set Database:World:ConnectionString in appsettings.Design.json or Database__World__ConnectionString env var.");

    var opts = Options.Create(new DatabaseConfiguration
    {
        World = new DatabaseConnection { ConnectionString = conn }
    });

    return new WorldDbContext(NullLoggerFactory.Instance, opts);
}
