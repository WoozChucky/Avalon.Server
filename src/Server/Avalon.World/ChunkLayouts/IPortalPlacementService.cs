using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Entities;

namespace Avalon.World.ChunkLayouts;

public interface IPortalPlacementService
{
    /// <summary>
    /// Places portals from <paramref name="layout"/> on <paramref name="sink"/>. <paramref name="cfg"/>
    /// is nullable so predefined town layouts (which carry no <see cref="ProceduralMapConfig"/>) can be
    /// placed directly. Forward-portal config defaults come from <paramref name="cfg"/> when present;
    /// when null only the portals already enumerated on <see cref="ChunkLayout.Portals"/> are emitted.
    /// </summary>
    void Place(IPortalSink sink, ChunkLayout layout, ProceduralMapConfig? cfg);
}

public class PortalPlacementService : IPortalPlacementService
{
    private uint _nextGuid = 1;

    public void Place(IPortalSink sink, ChunkLayout layout, ProceduralMapConfig? cfg)
    {
        // ChunkLayout.Portals is already pre-resolved by the source (procedural or predefined),
        // so no BossChunk deref is required here. cfg is accepted-but-unused to keep the door
        // open for future portal config derived from procedural settings.
        _ = cfg;
        foreach (var p in layout.Portals)
        {
            var guid = new ObjectGuid(ObjectType.Portal, _nextGuid++);
            sink.AddPortal(new PortalInstance(guid, p.WorldPos, p.Radius, p.TargetMapId, (byte)p.Role));
        }
    }
}
