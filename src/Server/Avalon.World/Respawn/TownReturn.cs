using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
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
/// RespawnInFlight when it arrives, and when it fails (a town lookup or an instance build that faults, or a step
/// that throws), which is logged at Error, or, when the caller passes <c>failed</c>, handed to it instead (the party
/// leave countdown retries a failed return, #700).
/// </summary>
public sealed class TownReturn(ILogger logger, IWorld world, IRespawnTargetResolver resolver, IChunkLibrary chunkLibrary)
{
    /// <param name="failed">
    /// Called on the tick, once, with the failure, after RespawnInFlight is cleared; it then owns the logging. Never
    /// called for a return that arrived or that was dropped because the character left the connection.
    /// </param>
    public void Start(IWorldConnection connection, bool revive, bool dropEncounter, Action<Exception>? failed = null)
    {
        if (connection.Character is not { } ch)
        {
            return;
        }

        Task<MapTemplateId> town;
        try
        {
            town = resolver.ResolveTownAsync(new MapTemplateId(ch.Map.Value), CancellationToken.None);
        }
        catch (Exception e)
        {
            Failed(connection, ch, e, failed);
            return;
        }

        Then(connection, ch, town, failed,
            townMapId => OnTownResolved(connection, ch, townMapId, revive, dropEncounter, failed));
    }

    private void OnTownResolved(IWorldConnection connection, ICharacter ch, MapTemplateId townMapId, bool revive,
        bool dropEncounter, Action<Exception>? failed)
    {
        ushort maxPlayers = world.MapTemplates.FirstOrDefault(t => t.Id == townMapId)?.MaxPlayers ?? 30;

        Then(connection, ch, world.InstanceRegistry.GetOrCreateTownInstanceAsync(townMapId, (ushort)maxPlayers), failed,
            townInstance => OnInstanceReady(connection, ch, townMapId, townInstance, revive, dropEncounter));
    }

    /// <summary>
    /// Runs <paramref name="callback" /> on the tick once <paramref name="task" /> has succeeded, and otherwise ends the
    /// return as failed. The connection's continuation drain logs and drops a faulted task's callback, which would
    /// leave RespawnInFlight set for good, so the task is settled first and the callback always runs, as
    /// CommandContext.Then does for chat commands.
    /// </summary>
    private void Then<T>(IWorldConnection connection, ICharacter ch, Task<T> task, Action<Exception>? failed,
        Action<T> callback) =>
        connection.EnqueueContinuation(Settled(task), () =>
        {
            if (!task.IsCompletedSuccessfully)
            {
                Failed(connection, ch, FailureOf(task), failed);
                return;
            }

            try
            {
#pragma warning disable MA0045 // the task has completed successfully: reading its result does not block
                callback(task.Result);
#pragma warning restore MA0045
            }
            catch (Exception e)
            {
                Failed(connection, ch, e, failed);
            }
        });

    /// <summary>
    /// The return failed: the in-flight flag cleared so a later death can respawn, while the connection still holds
    /// the character it was for (after a character leave the flag belongs to the next character), then logged, or
    /// handed to the caller's <paramref name="failed" />, contained so its throw is logged rather than lost.
    /// </summary>
    private void Failed(IWorldConnection connection, ICharacter ch, Exception e, Action<Exception>? failed)
    {
        if (ReferenceEquals(connection.Character, ch))
            connection.RespawnInFlight = false;

        if (failed is null)
        {
            logger.LogError(e, "Return to town of {Name} failed", ch.Name);
            return;
        }

        try
        {
            failed(e);
        }
        catch (Exception callbackError)
        {
            logger.LogError(new AggregateException(e, callbackError), "Return to town of {Name} failed, and so did its failure handler",
                ch.Name);
        }
    }

    private static Task Settled(Task task) =>
        task.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static Exception FailureOf(Task task) =>
        task.Exception is { InnerExceptions.Count: 1 } single ? single.InnerExceptions[0]
        : task.Exception is { } many ? many
        : new TaskCanceledException(task);

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

        try
        {
            if (dropEncounter)
                world.InstanceRegistry.GetInstanceById(ch.InstanceId)?.CombatService.DropPlayerFromEncounter(ch);

            // Transfer first so MapInstance.AddCharacter is the boundary that enables broadcast.
            // The map transition and chunk layout must go out in this same callback: MapInstance sends
            // the loot snapshot on its next tick, and the client has to know the map before its drops.
            world.TransferPlayer(connection, townInstance);

            // Resolve spawn coords from the town's chunk layout. Fall back to template defaults defensively.
            float spawnX, spawnY, spawnZ;
            MapTemplate townTemplate = world.MapTemplates.First(t => t.Id == townMapId);
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
            // method so the next broadcast tick emits "alive + full HP" together. A countdown member who
            // was alive when its return started and died before it arrived arrives alive too.
            if (revive || ch.IsDead)
                ch.Revive();

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
            // its layout to the client, which bakes the same navmesh from it and builds the map's
            // geometry and portals. Town instances always have a Layout; fallback is defensive.
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
        finally
        {
            // Cleared on every path, a throw included, so the player can die and respawn again on a future
            // engagement. A throw is logged by the caller (Then), which clears it too.
            connection.RespawnInFlight = false;
        }
    }
}
