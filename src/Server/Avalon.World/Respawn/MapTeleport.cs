using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Maps.Navigation;
using Avalon.World.Parties;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Respawn;

/// <summary>
/// Moves a living character to any map (item use): the instance is resolved as a portal resolves it (a town's
/// shared one; in a party, the party's instance of a Normal map, within its capacity; otherwise the character's own),
/// the map's level band refuses as a portal's does, and the move goes through World.TransferPlayer after dropping the
/// character from its encounter. A given position is snapped to the nearest walkable navmesh point within the
/// navigator's ground box (half-extents 2 m on X/Z, 4 m on Y), and no mesh there refuses the arrival with
/// NoWalkableGround (an item that started the move stays consumed); with no position given, the character arrives at
/// the instance's entry spawn. The character is saved on arrival, as a portal entry saves it. Tick thread;
/// the instance build runs off it and the arrival comes back as a connection continuation. RespawnInFlight marks the
/// move under way, so it cannot overlap a respawn, a party return or a second teleport; it is cleared on every ending.
/// </summary>
public sealed class MapTeleport(ILogger<MapTeleport> logger, IWorld world, IChunkLibrary chunkLibrary,
    ICharacterSaver saver, PartyService? parties = null)
{
    /// <summary>False, changing nothing, for no character, a move under way, an unknown map or a level outside its band.</summary>
    public bool Start(IWorldConnection connection, MapTemplateId map, Vector3? position)
    {
        if (connection.Character is not { } character || connection.RespawnInFlight)
            return false;

        MapTemplate? template = world.MapTemplates.FirstOrDefault(t => t.Id == map);
        if (template is null
            || (template.MinLevel is { } min && character.Level < min)
            || (template.MaxLevel is { } max && character.Level > max))
            return false;

        Task<IMapInstance> instance;
        PartyId? partyId = null;
        int capacity = int.MaxValue;
        if (template.MapType == MapType.Town)
        {
            instance = world.InstanceRegistry.GetOrCreateTownInstanceAsync(template.Id, template.MaxPlayers ?? 30);
        }
        else if (parties?.PartyOf(character.Guid.Id) is { } party)
        {
            partyId = party.Id;
            capacity = Math.Min(world.Configuration.MaxPartySize, template.MaxPlayers ?? world.Configuration.MaxPartySize);
            instance = world.PartyInstances.GetOrCreatePartyInstanceAsync(party.Id, template.Id);
        }
        else
        {
            instance = world.InstanceRegistry.GetOrCreateNormalInstanceAsync(character.Guid.Id, template.Id);
        }

        connection.RespawnInFlight = true;
        connection.EnqueueContinuation(Settled(instance),
            () => Arrive(connection, character, instance, template, position, partyId, capacity));
        return true;
    }

    private void Arrive(IWorldConnection connection, ICharacter character, Task<IMapInstance> built, MapTemplate template,
        Vector3? position, PartyId? partyId, int capacity)
    {
        // The character can leave the connection while the instance builds (a select elsewhere kicks it): the flag
        // then belongs to the next character, and nothing is moved.
        if (!ReferenceEquals(connection.Character, character))
            return;

        try
        {
            // A character that died while the instance built is not moved: no transfer, no save.
            if (character.IsDead)
            {
                logger.LogDebug("Teleport of {Name} to map {Map} dropped: the character died before it arrived",
                    character.Name, template.Id.Value);
                return;
            }

            if (!built.IsCompletedSuccessfully)
            {
                logger.LogError(built.Exception, "Teleport of {Name} to map {Map} failed: its instance could not be built",
                    character.Name, template.Id.Value);
                return;
            }

#pragma warning disable MA0045 // the task has completed successfully: reading its result does not block
            IMapInstance target = built.Result;
#pragma warning restore MA0045

            if (PartyRefusal(character, target, partyId, capacity) is { } refusal)
            {
                connection.Send(SMapTransitionPacket.CreateFailure(refusal, connection.CryptoSession.Encrypt));
                return;
            }

            Vector3 at = MapArrival.SpawnOf(target, template);
            if (position is { } wanted && !TrySnap(target, wanted, out at))
            {
                logger.LogWarning("Teleport of {Name} to map {Map} refused: no walkable ground near {Position}",
                    character.Name, template.Id.Value, wanted);
                connection.Send(SMapTransitionPacket.CreateFailure(MapTransitionResult.NoWalkableGround,
                    connection.CryptoSession.Encrypt));
                return;
            }

            world.InstanceRegistry.GetInstanceById(character.InstanceId)?.CombatService.DropPlayerFromEncounter(character);
            world.TransferPlayer(connection, target);

            character.Position = at;
            MapArrival.Send(connection, target, template, at, chunkLibrary);
            SaveOnArrival(connection, target, template, at);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Teleport of {Name} to map {Map} failed", character.Name, template.Id.Value);
        }
        finally
        {
            connection.RespawnInFlight = false;
        }
    }

    /// <summary>
    /// For a party instance, as a portal entry checks it: a character no longer in that party is refused MapNotFound,
    /// then a full instance InstanceFull. Null when the arrival may go on.
    /// </summary>
    private MapTransitionResult? PartyRefusal(ICharacter character, IMapInstance target, PartyId? partyId, int capacity)
    {
        if (partyId is not { } party)
            return null;

        // The party can end, or the character leave it, while the instance builds.
        if (parties?.PartyOf(character.Guid.Id)?.Id.Equals(party) != true)
            return MapTransitionResult.MapNotFound;

        if (target.PlayerCount >= capacity)
            return MapTransitionResult.InstanceFull;

        return null;
    }

    /// <summary>Saved on arrival, as a portal entry is (EnterMapHandler step 13): map, instance and position on the row.</summary>
    private void SaveOnArrival(IWorldConnection connection, IMapInstance target, MapTemplate template, Vector3 at)
    {
        if (connection.Character is not CharacterEntity { Data: { } row } entity)
            return;

        row.Map = new MapId(template.Id.Value);
        row.InstanceId = target.InstanceId.ToString();
        row.X = at.x;
        row.Y = at.y;
        row.Z = at.z;
        connection.EnqueueContinuation(saver.Save(connection, entity), committed =>
            logger.LogInformation("Character {Name} teleported to map {Map} instance {Instance}; save committed: {Committed}",
                entity.Name, template.Id.Value, target.InstanceId, committed));
    }

    /// <summary>
    /// The nearest walkable point to <paramref name="wanted" />, by the navigator's ground query, the one
    /// creature placement uses (#720): its box has half-extents 2 m on X/Z and 4 m on Y. False when no mesh is in the
    /// box. A navigator that cannot tell (no IGroundNavigator) snaps only the height; a map with no navmesh keeps the point.
    /// </summary>
    public static bool TrySnap(IMapInstance target, Vector3 wanted, out Vector3 at)
    {
        IMapNavigator navigator = target.GetNavigatorForPosition(wanted);
        if (navigator is not IGroundNavigator ground)
        {
            at = wanted;
            at.y = navigator.SampleGroundHeight(wanted.x, wanted.y, wanted.z);
            return true;
        }

        switch (ground.FindGround(wanted, out at))
        {
            case NavmeshGroundKind.Under or NavmeshGroundKind.Nearest:
                return true;
            case NavmeshGroundKind.NoNavMesh:
                at = wanted;
                return true;
            default:
                at = wanted;
                return false;
        }
    }

    private static Task Settled(Task task) =>
        task.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
