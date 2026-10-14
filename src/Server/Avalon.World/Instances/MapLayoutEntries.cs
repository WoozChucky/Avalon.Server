using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;

namespace Avalon.World.Instances;

/// <summary>
/// One instance's layout as <c>SMSG_CHUNK_LAYOUT</c> carries it (#883): an entry per chunk, with the chunk's name, and
/// an entry per portal. Built once, with the instance (so off the tick, where the instance is built), and written
/// into the packet of every character that arrives, where each arrival built them again, on the tick, in four copies
/// of one block.
/// </summary>
/// <remarks>
/// <para>
/// What it is built from does not change while the instance lives: an instance keeps the <see cref="ChunkLayout" /> it
/// was built from (a town's from its placement rows, a procedural map's from its seed), and the chunk library that
/// names the chunks is loaded once, with the world. Neither is an area <c>/reload</c> or live template editing
/// replaces.
/// </para>
/// <para>
/// Nothing changes it once it is built, and nothing outside it holds the two lists: <see cref="Encode" /> hands them
/// to the packet's thread-local message, which holds them while it is written and lets go of them when it is, or
/// fails to be (<c>PacketEncoder</c> clears the message's own members, never an entry's). It is complete before its
/// instance's constructor returns, so before the instance is published to the tick, where every arrival reads it.
/// It holds no pooled or shared resource: an instance whose build fails later, or that nobody ever enters, leaves
/// nothing behind but memory the collector takes with it.
/// </para>
/// </remarks>
internal sealed class MapLayoutEntries
{
    private readonly Guid _instanceId;
    private readonly ChunkLayout _layout;
    private readonly List<PlacedChunkDto> _chunks;
    private readonly List<PortalPlacementDto> _portals;

    /// <summary>
    /// The entries of <paramref name="layout" />, in its order, for the instance <paramref name="instanceId" />;
    /// throws for a chunk <paramref name="chunkLibrary" /> does not hold.
    /// </summary>
    public MapLayoutEntries(Guid instanceId, ChunkLayout layout, IChunkLibrary chunkLibrary)
    {
        IReadOnlyList<PlacedChunk> placed = layout.Chunks;
        var chunks = new List<PlacedChunkDto>(placed.Count);
        for (int i = 0; i < placed.Count; i++)
        {
            PlacedChunk chunk = placed[i];
            chunks.Add(new PlacedChunkDto
            {
                ChunkTemplateId = chunk.TemplateId.Value,
                ChunkName = chunkLibrary.GetById(chunk.TemplateId).Name,
                GridX = chunk.GridX,
                GridZ = chunk.GridZ,
                Rotation = chunk.Rotation,
            });
        }

        IReadOnlyList<PortalPlacement> placements = layout.Portals;
        var portals = new List<PortalPlacementDto>(placements.Count);
        for (int i = 0; i < placements.Count; i++)
        {
            PortalPlacement portal = placements[i];
            portals.Add(new PortalPlacementDto
            {
                Role = (byte)portal.Role,
                WorldPos = Vector3Dto.From(portal.WorldPos),
                Radius = portal.Radius,
                TargetMapId = portal.TargetMapId,
            });
        }

        _instanceId = instanceId;
        _layout = layout;
        _chunks = chunks;
        _portals = portals;
    }

    /// <summary>The layout's packet, as the layout of map <paramref name="mapId" />.</summary>
    public OutboundPacket Encode(ushort mapId, PacketEncoder encoder) =>
        SChunkLayoutPacket.Create(_layout.Seed, _instanceId, mapId, _layout.CellSize, _chunks, _layout.EntrySpawnWorldPos,
            _portals, encoder);
}
