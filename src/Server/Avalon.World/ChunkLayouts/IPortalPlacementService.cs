using Avalon.Common;
using Avalon.Domain.World;
using Avalon.World.Entities;

namespace Avalon.World.ChunkLayouts;

public interface IPortalPlacementService
{
    void Place(IPortalSink sink, ChunkLayout layout, ProceduralMapConfig cfg);
}

public class PortalPlacementService : IPortalPlacementService
{
    private uint _nextGuid = 1;

    public void Place(IPortalSink sink, ChunkLayout layout, ProceduralMapConfig cfg)
    {
        foreach (var p in layout.Portals)
        {
            var guid = new ObjectGuid(ObjectType.Portal, _nextGuid++);
            sink.AddPortal(new PortalInstance(guid, p.WorldPos, p.Radius, p.TargetMapId, (byte)p.Role));
        }
    }
}
