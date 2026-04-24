using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Procedural;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Procedural;

public class PortalPlacementServiceShould
{
    [Fact]
    public void Place_back_portal_at_entry_chunk_with_back_target_map_id()
    {
        var captured = new List<PortalInstance>();
        var sink = Substitute.For<IPortalSink>();
        sink.When(s => s.AddPortal(Arg.Any<PortalInstance>()))
            .Do(ci => captured.Add(ci.Arg<PortalInstance>()));

        var layout = new ProceduralLayout(
            Seed: 1,
            Chunks: new[] { new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero) },
            EntryChunk: new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero),
            BossChunk: null,
            Portals: new[] { new PortalPlacement(PortalRole.Back, new Vector3(10, 0, 10), 42) },
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f);
        var cfg = new ProceduralMapConfig { BackPortalTargetMapId = 42 };

        var svc = new PortalPlacementService();
        svc.Place(sink, layout, cfg);

        Assert.Single(captured);
        Assert.Equal((ushort)42, captured[0].TargetMapId);
        Assert.Equal(0, captured[0].Role); // 0 = Back
        Assert.Equal(new Vector3(10, 0, 10), captured[0].Position);
    }

    [Fact]
    public void Place_back_and_forward_portals_when_both_configured()
    {
        var captured = new List<PortalInstance>();
        var sink = Substitute.For<IPortalSink>();
        sink.When(s => s.AddPortal(Arg.Any<PortalInstance>()))
            .Do(ci => captured.Add(ci.Arg<PortalInstance>()));

        var layout = new ProceduralLayout(
            Seed: 1,
            Chunks: Array.Empty<PlacedChunk>(),
            EntryChunk: new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero),
            BossChunk: new PlacedChunk(new ChunkTemplateId(2), 1, 0, 0, new Vector3(30, 0, 0)),
            Portals: new[]
            {
                new PortalPlacement(PortalRole.Back, new Vector3(10, 0, 10), 1),
                new PortalPlacement(PortalRole.Forward, new Vector3(40, 0, 10), 99),
            },
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f);
        var cfg = new ProceduralMapConfig { BackPortalTargetMapId = 1, ForwardPortalTargetMapId = 99 };

        var svc = new PortalPlacementService();
        svc.Place(sink, layout, cfg);

        Assert.Equal(2, captured.Count);
        Assert.Contains(captured, p => p.TargetMapId == 1 && p.Role == 0);
        Assert.Contains(captured, p => p.TargetMapId == 99 && p.Role == 1);
    }
}
