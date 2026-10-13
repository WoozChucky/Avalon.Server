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
/// What a client is told when its character arrives in an instance, in one place for every arrival (#883): a portal
/// entry (EnterMapHandler), a return to town (TownReturn) and an item's teleport (MapTeleport) send the transition,
/// then the chunk layout; a select (CharacterSelectHandler) sends its own answer, then the same chunk layout. All in
/// the same callback as the transfer, so the client knows the map before the instance's next tick sends what lies on
/// it.
/// </summary>
public static class MapArrival
{
    /// <summary>
    /// The instance's entry spawn, or the template's default spawn for an instance that is not a
    /// <see cref="MapInstance" />.
    /// </summary>
    public static Vector3 SpawnOf(IMapInstance instance, MapTemplate template) =>
        instance is MapInstance built
            ? built.EntrySpawnWorldPos
            : new Vector3(template.DefaultSpawnX, template.DefaultSpawnY, template.DefaultSpawnZ);

    /// <summary>
    /// Sends <paramref name="connection" /> a successful transition into <paramref name="instance" /> at
    /// <paramref name="at" />, then, for a <see cref="MapInstance" />, its chunk layout (its chunks and portals).
    /// Call it in the same callback as the transfer.
    /// </summary>
    public static void Send(IWorldConnection connection, IMapInstance instance, MapTemplate template, Vector3 at,
        IChunkLibrary chunkLibrary)
    {
        connection.Send(SMapTransitionPacket.Create(MapTransitionResult.Success, instance.InstanceId, template.Id.Value,
            at.x, at.y, at.z, template.Name, template.Description, PacketEncoder.Shared));

        SendLayout(connection, instance, template.Id.Value, chunkLibrary);
    }

    /// <summary>
    /// Sends <paramref name="connection" /> the chunk layout of <paramref name="instance" />, as the layout of map
    /// <paramref name="mapId" />: the seed, the cell size, the chunks, the entry spawn and the portals a client builds
    /// the map and bakes its navmesh from. Nothing for an instance that is not a <see cref="MapInstance" />: every
    /// instance the server builds is one, and has a layout. The chunk and portal entries are the instance's own, built
    /// at its first arrival (<see cref="MapInstance.LayoutPacket" />).
    /// </summary>
    public static void SendLayout(IWorldConnection connection, IMapInstance instance, ushort mapId,
        IChunkLibrary chunkLibrary)
    {
        if (instance is MapInstance built)
            connection.Send(built.LayoutPacket(mapId, chunkLibrary, PacketEncoder.Shared));
    }
}
