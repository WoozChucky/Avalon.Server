using Avalon.Common.Mathematics;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;

namespace Avalon.World.ChunkLayouts;

public interface ICreaturePlacementService
{
    Task PlaceAsync(IMapInstance instance, ChunkLayout layout, ProceduralMapConfig cfg, int seed, CancellationToken ct);
}

public class CreaturePlacementService : ICreaturePlacementService
{
    private readonly ICreatureSpawner _spawner;
    private readonly IChunkLibrary _library;
    private readonly ISpawnTableRepository _spawnTableRepo;

    public CreaturePlacementService(ICreatureSpawner spawner, IChunkLibrary library, ISpawnTableRepository spawnTableRepo)
    {
        _spawner = spawner;
        _library = library;
        _spawnTableRepo = spawnTableRepo;
    }

    public async Task PlaceAsync(IMapInstance instance, ChunkLayout layout, ProceduralMapConfig cfg, int seed, CancellationToken ct)
    {
        var table = await _spawnTableRepo.FindByIdAsync(cfg.SpawnTableId, track: false, ct)
                    ?? throw new InvalidProceduralConfigException($"SpawnTable {cfg.SpawnTableId.Value} not found");

        var entriesByTag = table.Entries
            .GroupBy(e => e.Tag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rng = new Random(seed);

        foreach (var chunk in layout.Chunks)
        {
            var tpl = _library.GetById(chunk.TemplateId);
            foreach (var slot in tpl.SpawnSlots)
            {
                if (slot.Tag.Equals("empty", StringComparison.OrdinalIgnoreCase)) continue;
                if (slot.Tag.Equals("entry", StringComparison.OrdinalIgnoreCase)) continue;
                if (!entriesByTag.TryGetValue(slot.Tag, out var entries) || entries.Count == 0) continue;

                var entry = WeightedPick(entries, rng);
                int count = rng.Next(entry.MinCount, entry.MaxCount + 1);
                var worldPos = RotateLocal(slot.LocalX, slot.LocalY, slot.LocalZ, chunk);

                for (int i = 0; i < count; i++)
                {
                    var creatureInfo = new CreatureInfo
                    {
                        Position = worldPos,
                        PrototypeIndex = entry.CreatureId.Value,
                    };
                    var creature = _spawner.Spawn(creatureInfo);
                    instance.AddCreature(creature);
                }
            }
        }
    }

    private static Vector3 RotateLocal(float lx, float ly, float lz, PlacedChunk chunk)
    {
        (float rx, float rz) = chunk.Rotation switch
        {
            0 => (lx, lz),
            1 => (lz, -lx),
            2 => (-lx, -lz),
            3 => (-lz, lx),
            _ => (lx, lz),
        };
        return new Vector3(chunk.WorldPos.x + rx, chunk.WorldPos.y + ly, chunk.WorldPos.z + rz);
    }

    private static SpawnTableEntry WeightedPick(IList<SpawnTableEntry> items, Random rng)
    {
        float total = items.Sum(i => i.Weight);
        float r = (float)(rng.NextDouble() * total);
        foreach (var i in items)
        {
            r -= i.Weight;
            if (r <= 0) return i;
        }
        return items[^1];
    }
}
