using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Procedural;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

public class ProceduralLayoutGeneratorShould
{
    // Bit layout: side order N(0..2), E(3..5), S(6..8), W(9..11). Slot: L,C,R within side.
    // N-Center = bit 1 (0b_0000_0000_0000_0010)
    // E-Center = bit 4 (0b_0000_0000_0001_0000)
    // S-Center = bit 7 (0b_0000_0000_1000_0000)
    // W-Center = bit 10 (0b_0000_0100_0000_0000)

    private static ChunkTemplate MakeChunk(int id, string slotTag, ushort exits, PortalRole? portal = null)
    {
        var t = new ChunkTemplate
        {
            Id = new ChunkTemplateId(id),
            Name = $"c{id}",
            AssetKey = $"Chunks/c{id}",
            GeometryFile = $"Chunks/c{id}.obj",
            CellSize = 30f,
            Exits = exits,
        };
        if (!string.IsNullOrEmpty(slotTag))
            t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = slotTag, LocalX = 15, LocalY = 0, LocalZ = 15 });
        if (portal is not null)
            t.PortalSlots.Add(new ChunkPortalSlot { Role = portal.Value, LocalX = 10, LocalY = 0, LocalZ = 10 });
        return t;
    }

    [Fact]
    public void Generate_is_deterministic_for_same_seed()
    {
        // Entry chunk: has N-exit, Spawn_Entry and Portal_Back slots.
        var entry = MakeChunk(1, "entry", exits: 0b_0000_0000_0000_0010, portal: PortalRole.Back);
        entry.SpawnSlots.Add(new ChunkSpawnSlot { Tag = "entry", LocalX = 5, LocalY = 0, LocalZ = 5 });
        // Mid chunk: N+S centers (corridor).
        var mid = MakeChunk(2, "pack", exits: 0b_0000_0000_1000_0010);
        // Boss chunk: has S-center, Spawn_Boss slot, Portal_Forward slot.
        var boss = MakeChunk(3, "boss", exits: 0b_0000_0000_1000_0000, portal: PortalRole.Forward);

        var pool = new List<ChunkPoolMember>
        {
            new(entry, 1f), new(mid, 1f), new(boss, 1f)
        };
        var config = new ProceduralMapConfig
        {
            MapTemplateId = new MapTemplateId(10),
            ChunkPoolId = new ChunkPoolId(1),
            SpawnTableId = new SpawnTableId(1),
            MainPathMin = 3, MainPathMax = 3,
            BranchChance = 0, BranchMaxDepth = 0,
            HasBoss = true,
            BackPortalTargetMapId = 1,
            ForwardPortalTargetMapId = 99,
        };

        var gen = new ProceduralLayoutGenerator(NullLoggerFactory.Instance);
        var a = gen.Generate(config, pool, seed: 42);
        var b = gen.Generate(config, pool, seed: 42);

        Assert.Equal(a.Chunks.Count, b.Chunks.Count);
        Assert.Equal(a.BossChunk!.TemplateId.Value, b.BossChunk!.TemplateId.Value);
        Assert.Equal(a.EntrySpawnWorldPos, b.EntrySpawnWorldPos);
    }
}
