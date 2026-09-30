using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Respawn;

/// <summary>
/// Moves a character to the respawn town of the map it is on: resolve the town, get its instance, transfer, then
/// the transition and the chunk layout in the same callback. The respawn handler revives on arrival; a party leave
/// countdown (2026-09-30) moves a living character and drops it from its encounter first. Tick thread; clears
/// RespawnInFlight when it arrives.
/// </summary>
public sealed class TownReturn(ILogger logger, IWorld world, IRespawnTargetResolver resolver, IChunkLibrary chunkLibrary)
{
    public void Start(IWorldConnection connection, bool revive, bool dropEncounter)
    {
        if (connection.Character is not { } ch)
        {
            return;
        }

        connection.EnqueueContinuation(
            resolver.ResolveTownAsync(new MapTemplateId(ch.Map.Value), CancellationToken.None),
            townMapId => OnTownResolved(connection, ch, townMapId, revive, dropEncounter));
    }

    private void OnTownResolved(IWorldConnection connection, ICharacter ch, MapTemplateId townMapId, bool revive,
        bool dropEncounter)
    {
        var maxPlayers = world.MapTemplates.FirstOrDefault(t => t.Id == townMapId)?.MaxPlayers ?? 30;

        connection.EnqueueContinuation(
            world.InstanceRegistry.GetOrCreateTownInstanceAsync(townMapId, (ushort)maxPlayers),
            townInstance => OnInstanceReady(connection, ch, townMapId, townInstance, revive, dropEncounter));
    }

    private void OnInstanceReady(IWorldConnection connection, ICharacter ch, MapTemplateId townMapId,
        IMapInstance townInstance, bool revive, bool dropEncounter)
    {
        // The character can leave the connection while the town loads: a select of it on another
        // connection despawns it here and kicks this one. Transferring then would dereference a
        // character the connection no longer holds, and reviving would touch a discarded entity.
        if (!ReferenceEquals(connection.Character, ch))
        {
            logger.LogDebug("Dropped respawn of {Name}: the character left the connection before the town was ready",
                ch.Name);
            return;
        }

        if (dropEncounter)
            world.InstanceRegistry.GetInstanceById(ch.InstanceId)?.CombatService.DropPlayerFromEncounter(ch);

        // Transfer first so MapInstance.AddCharacter is the boundary that enables broadcast.
        // The map transition and chunk layout must go out in this same callback: MapInstance sends
        // the loot snapshot on its next tick, and the client has to know the map before its drops.
        world.TransferPlayer(connection, townInstance);

        // Resolve spawn coords from the town's chunk layout. Fall back to template defaults defensively.
        float spawnX, spawnY, spawnZ;
        var townTemplate = world.MapTemplates.First(t => t.Id == townMapId);
        if (townInstance is MapInstance mi && mi.EntrySpawnWorldPos is { } s)
        {
            spawnX = s.x; spawnY = s.y; spawnZ = s.z;
        }
        else
        {
            spawnX = townTemplate.DefaultSpawnX;
            spawnY = townTemplate.DefaultSpawnY;
            spawnZ = townTemplate.DefaultSpawnZ;
        }

        ch.Position = new Vector3(spawnX, spawnY, spawnZ);

        // Revive() atomically clears IsDead and restores HP. Both fields dirty in a single
        // method so the next broadcast tick emits "alive + full HP" together.
        if (revive)
            ch.Revive();

        // Clear the in-flight flag so the player can die + respawn again on a future engagement.
        connection.RespawnInFlight = false;

        // Send the standard transition packet the client already handles.
        connection.Send(SMapTransitionPacket.Create(
            MapTransitionResult.Success,
            townInstance.InstanceId,
            townMapId,
            spawnX, spawnY, spawnZ,
            townTemplate.Name,
            townTemplate.Description,
            connection.CryptoSession.Encrypt));

        // Mirror EnterMapHandler.OnInstanceReceived: every chunk-layout-built instance ships
        // its layout to the client so ClientMapNavigator can rebake the navmesh, the
        // ChunkLayoutVisualizer can repaint geometry, and PortalRuntimeSpawner can recreate
        // portal triggers. Town instances always have a Layout; fallback is defensive.
        if (townInstance is MapInstance layoutMi && layoutMi.Layout is { } layout)
        {
            var dtos = layout.Chunks.Select(c => new PlacedChunkDto
            {
                ChunkTemplateId = c.TemplateId.Value,
                ChunkName = chunkLibrary.GetById(c.TemplateId).Name,
                GridX = c.GridX,
                GridZ = c.GridZ,
                Rotation = c.Rotation,
            }).ToList();
            var portalDtos = layout.Portals.Select(p => new PortalPlacementDto
            {
                Role = (byte)p.Role,
                WorldPos = Vector3Dto.From(p.WorldPos),
                Radius = p.Radius,
                TargetMapId = p.TargetMapId,
            }).ToList();
            connection.Send(SChunkLayoutPacket.Create(
                layout.Seed,
                layoutMi.InstanceId,
                townMapId.Value,
                layout.CellSize,
                dtos,
                layout.EntrySpawnWorldPos,
                portalDtos,
                connection.CryptoSession.Encrypt));
        }

        logger.LogInformation("Character {Name} returned to town {Map} instance {Instance}",
            ch.Name, townMapId.Value, townInstance.InstanceId);
    }
}
