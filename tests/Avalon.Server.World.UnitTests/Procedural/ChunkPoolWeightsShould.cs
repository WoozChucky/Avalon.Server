using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Procedural;

/// <summary>
/// A pool member's weight and its per-layout cap (chunk-pools.json): a capped member is never placed more often than its
/// cap in one layout, and a pool with default weights and no caps generates exactly the layouts it always did.
/// </summary>
public class ChunkPoolWeightsShould
{
    private const ushort N = 0b_0000_0000_0000_0010, E = 0b_0000_0000_0001_0000, S = 0b_0000_0000_1000_0000, W = 0b_0000_0100_0000_0000;

    private static ChunkTemplate Chunk(int id, ushort exits, string? tag = null, PortalRole? portal = null)
    {
        var t = new ChunkTemplate { Id = new ChunkTemplateId(id), Name = $"c{id}", GeometryFile = $"Chunks/c{id}.obj", CellSize = 30f, Exits = exits };
        if (tag is not null) t.SpawnSlots.Add(new ChunkSpawnSlot { Tag = tag, LocalX = 15, LocalY = 1, LocalZ = 15 });
        if (portal is { } role) t.PortalSlots.Add(new ChunkPortalSlot { Role = role, LocalX = 15, LocalY = 1, LocalZ = 5 });
        return t;
    }

    private static List<ChunkPoolMember> Pool() =>
    [
        new(Chunk(1, N, "entry", PortalRole.Back), 1f),
        new(Chunk(2, (ushort)(N | S)), 1f),
        new(Chunk(3, (ushort)(E | W)), 1f),
        new(Chunk(4, (ushort)(N | E)), 1f),
        new(Chunk(5, (ushort)(S | E)), 1f),
        new(Chunk(6, (ushort)(N | E | S | W)), 1f),
        new(Chunk(7, S), 1f),   // a dead end, for the branches
    ];

    private static ChunkGroupDefinition Clearing() => new("clearing",
    [
        new ChunkGroupCell(Chunk(11, S), 0, 0), new ChunkGroupCell(Chunk(12, E), 1, 0),
        new ChunkGroupCell(Chunk(13, W), 0, 1), new ChunkGroupCell(Chunk(14, N), 1, 1),
    ]);

    private static ChunkGroupDefinition Arena() => new("arena",
    [
        new ChunkGroupCell(Chunk(21, S), 0, 0), new ChunkGroupCell(Chunk(22, 0), 1, 0),
        new ChunkGroupCell(Chunk(23, 0), 0, 1), new ChunkGroupCell(Chunk(24, 0, "boss"), 1, 1),
    ]);

    private static ProceduralMapConfig Config() => new()
    {
        MapTemplateId = new MapTemplateId(20),
        ChunkPoolId = new ChunkPoolId(1),
        SpawnTableId = new SpawnTableId(1),
        MainPathMin = 8,
        MainPathMax = 12,
        BranchChance = 0.5f,
        BranchMaxDepth = 2,
        HasBoss = true,
        BackPortalTargetMapId = 1,
        MinSetPieceStep = 3,
    };

    private static ChunkLayout? TryGenerate(List<ChunkPoolMember> pool, int seed)
    {
        try { return new ProceduralLayoutGenerator(NullLoggerFactory.Instance).Generate(Config(), pool, seed, [Clearing(), Arena()]); }
        catch (ProceduralGenerationFailedException) { return null; }
    }

    /// <summary>
    /// Each layout of this pool (single chunks, branches, a set piece and a boss set piece) as the generator made it before
    /// pool weights and caps reached the walk: the first 16 hex digits of the SHA-256 of every placed chunk in order.
    /// </summary>
    private static readonly string[] s_layoutsBeforeCaps =
    [
        "90CB37C68BB08165", "C7C41BE403468746", "14265488FD834F6D", "161D47E96587AC16",
        "0CCB96114D82DE57", "4E809DAC9D60998C", "8846611B019F20FB", "9026B3AFDDB05A0D",
        "7634624622D81BA2", "560ADB13C05F5163", "C44FFACB72F0343D", "183495D8446B6BF6",
        "647607CF076F92BD", "DEB29F7753A911BB", "3E99E483EC1F8862", "9C8E3FD0E894A799",
        "8107FBB3B285A8EA", "7D47111425D9F271", "5F07BBD08E34F5B0", "6B70E9CFEDA9B155",
        "BBD9CCCFA7899513", "8479F53383DB821E", "1EFF5E3C5A43E150", "3F44EE5FBE5F007E",
    ];

    [Fact]
    public void Generate_the_same_layouts_as_before_from_a_pool_without_caps()
    {
        string[] actual = Enumerable.Range(0, 24).Select(seed => Fingerprint(TryGenerate(Pool(), seed * 7919))).ToArray();

        Assert.True(s_layoutsBeforeCaps.SequenceEqual(actual), string.Join(", ", actual.Select(a => $"\"{a}\"")));
    }

    /// <summary>
    /// The crossroads, weighted far above every other member, takes the first step after the entry in nearly every layout
    /// (measured 191 of 200; 49 at weight 1), and its cap keeps it to one placement however often it is offered.
    /// </summary>
    [Fact]
    public void Place_a_heavy_member_capped_at_one_at_most_once_in_every_layout()
    {
        List<ChunkPoolMember> pool = Pool();
        pool[5] = pool[5] with { Weight = 50f, MaxPerLayout = 1 };
        int failed = 0, first = 0;

        for (int seed = 0; seed < 200; seed++)
        {
            if (TryGenerate(pool, seed) is not { } layout) { failed++; continue; }

            Assert.True(layout.Chunks.Count(c => c.TemplateId.Value == 6) <= 1, $"seed {seed}: the crossroads was placed twice");
            if (layout.Chunks.Any(c => c.TemplateId.Value == 6 && c.Depth == 1)) first++;
        }

        Assert.InRange(failed, 0, 10);
        Assert.InRange(first, 170, 200);
    }

    private static string Fingerprint(ChunkLayout? layout)
    {
        if (layout is null) return "failed";

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{layout.Seed}|{layout.MainPathLength};");
        foreach (PlacedChunk c in layout.Chunks)
            text.Append(CultureInfo.InvariantCulture, $"{c.TemplateId.Value},{c.GridX},{c.GridZ},{c.Rotation},{c.Depth},{c.Group};");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}
