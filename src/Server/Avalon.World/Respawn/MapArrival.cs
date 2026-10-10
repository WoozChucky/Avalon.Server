using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Instances;

namespace Avalon.World.Respawn;

/// <summary>
/// What a client is told when its character arrives in an instance (item use's teleport): the transition, then the
/// chunk layout, in the same callback as the transfer, as EnterMapHandler and TownReturn send them, so the client
/// knows the map before the instance's next tick sends what lies on it.
/// </summary>
public static class MapArrival
{
    /// <summary>The instance's entry spawn, or the template's default spawn for an instance with no layout.</summary>
    public static Vector3 SpawnOf(IMapInstance instance, MapTemplate template) =>
        instance is MapInstance { EntrySpawnWorldPos: { } entry }
            ? entry
            : new Vector3(template.DefaultSpawnX, template.DefaultSpawnY, template.DefaultSpawnZ);

    /// <summary>
    /// Sends <paramref name="connection" /> a successful transition into <paramref name="instance" /> at
    /// <paramref name="at" />, then, for an instance built from a chunk layout, that layout (its chunks and portals).
    /// Call it in the same callback as the transfer.
    /// </summary>
    public static void Send(IWorldConnection connection, IMapInstance instance, MapTemplate template, Vector3 at,
        IChunkLibrary chunkLibrary)
    {
        connection.Send(SMapTransitionPacket.Create(MapTransitionResult.Success, instance.InstanceId, template.Id.Value,
            at.x, at.y, at.z, template.Name, template.Description, PacketEncoder.Shared));

        if (instance is not MapInstance { Layout: { } layout } built)
            return;

        var chunks = layout.Chunks.Select(c => new PlacedChunkDto
        {
            ChunkTemplateId = c.TemplateId.Value,
            ChunkName = chunkLibrary.GetById(c.TemplateId).Name,
            GridX = c.GridX,
            GridZ = c.GridZ,
            Rotation = c.Rotation,
        }).ToList();
        var portals = layout.Portals.Select(p => new PortalPlacementDto
        {
            Role = (byte)p.Role,
            WorldPos = Vector3Dto.From(p.WorldPos),
            Radius = p.Radius,
            TargetMapId = p.TargetMapId,
        }).ToList();
        connection.Send(SChunkLayoutPacket.Create(layout.Seed, built.InstanceId, template.Id.Value, layout.CellSize, chunks,
            layout.EntrySpawnWorldPos, portals, PacketEncoder.Shared));
    }
}
