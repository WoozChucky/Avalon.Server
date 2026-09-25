using Avalon.World.Public;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Database.Character.Repositories;
using Avalon.Database.World.Repositories;
using Avalon.Domain.Auth;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Persistence;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Avalon.World.Abilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Avalon.Network.Packets.State;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Generic;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_SELECTED)]
public class CharacterSelectHandler(
    ILogger<CharacterSelectHandler> logger,
    ILoggerFactory loggerFactory,
    ICharacterRepository characterRepository,
    ICharacterInventoryRepository characterInventoryRepository,
    IItemInstanceRepository itemInstanceRepository,
    ICharacterAbilityRepository characterAbilityRepository,
    IChunkLibrary chunkLibrary,
    IWorld world,
    IRespawnTargetResolver respawnTargetResolver,
    IOptions<RegenConfiguration> regenConfig,
    IAccountRepository accountRepository,
    ICharacterSaver characterSaver,
    IWorldServer worldServer) : WorldPacketHandler<CCharacterSelectedPacket>
{
    private Activity? _parentActivity;

    /// <summary>
    /// The select step each connection has in flight, for a select that kicks it to wait on. Tick
    /// thread only, like the rest of the handler; weak, so it holds on to no closed connection.
    /// </summary>
    private readonly ConditionalWeakTable<IWorldConnection, Task> _selectStepInFlight = new();

    /// <summary>
    /// How long a select waits for the character's previous saves before giving up. Past it the
    /// select fails without reading, and the client can select again. Well inside
    /// <see cref="GameConfiguration.CharacterLoadTimeoutSeconds" />, which cancels the whole select,
    /// so a slow save leaves the rest of the load time to the reads.
    /// </summary>
    public TimeSpan SaveWaitLimit { get; init; } = TimeSpan.FromSeconds(5);

    public override void Execute(IWorldConnection connection, CCharacterSelectedPacket packet)
    {
        using Activity? activity =
            DiagnosticsConfig.World.Source.StartActivity(nameof(CharacterSelectHandler), ActivityKind.Server);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag(nameof(packet.CharacterId), packet.CharacterId);

        if (connection.AccountId == null)
        {
            logger.LogWarning(
                "Connection tried to select a character from the character list without being authenticated");
            activity?.AddEvent(new ActivityEvent("UnauthorizedSelectionAttempt"));
            connection.Close();
            return;
        }

        // Three states, not one. Character covers a spawned player; PendingSpawn a built one
        // waiting on its client; SelectInProgress the several database round trips between, where
        // both of the others are still null. A second select inside that span orphans the entity
        // the first one is building.
        if (connection.Character != null || connection.PendingSpawn != null || connection.SelectInProgress)
        {
            logger.LogWarning("Connection tried to select a character list while already having a character selected");
            activity?.AddEvent(new ActivityEvent("DuplicateSelectionAttempt"));
            connection.Close();
            return;
        }

        IReadOnlyList<Task> kickedWork = TakeOverFromOtherSessions(connection, connection.AccountId, packet.CharacterId);
        if (kickedWork.Count > 0)
            activity?.AddEvent(new ActivityEvent("WaitingOnKickedSessions"));

        // The select's identity. Every step of the chain checks it before doing anything, so a
        // select that is cancelled or kicked stops at its next step (see OwnsSelect).
        long select = DateTime.UtcNow.Ticks;
        connection.BeginSelect(select);

        Step(connection, select,
            FindAfterSavesAsync(packet.CharacterId, connection.AccountId, kickedWork),
            found =>
            {
                if (found.SaveStillRunning)
                {
                    // Nothing was read and nothing was built: the select simply did not happen.
                    connection.CancelSelect();
                    return;
                }

                OnCharacterReceived(connection, select, found.Character);
            });

        // Locale for dialogue text. Independent of the select chain: the default is enUS, so a slow
        // or failed read costs English text rather than correctness. TODO-029 wants the world's
        // dependency on the Auth database removed, but that is a 2.0 milestone and reading it here
        // is the sanctioned approach until then.
        connection.EnqueueContinuation(
            accountRepository.FindByIdAsync(connection.AccountId!, false, CancellationToken.None),
            (Account? account) =>
            {
                if (account is null)
                {
                    logger.LogWarning("No account {AccountId} for account lookup; leaving {Locale}",
                        connection.AccountId, connection.Locale);
                    return;
                }

                connection.Locale = account.Locale;

                if (connection is IAccessLevelAssignable assignable)
                {
                    assignable.AssignAccessLevel(account.AccessLevel);
                }
            });

        _parentActivity = activity;
    }

    /// <summary>
    /// One world session per account (#474). Selecting a character ends every other world session
    /// of the same account, whatever it holds: another character, this one, a select still under
    /// way, or nothing yet. Two live copies of one character would each hold their own inventory and
    /// money, and whichever saved last would write its copy over the other's; two characters of one
    /// account live at once is what the owner ruled out on top of that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each kicked character is despawned here, on the tick, rather than being left to the kicked
    /// connection's close. A close is only despawned when a later tick dequeues it, so a select
    /// waiting on <see cref="ICharacterSaver.WhenIdle" /> right after the kick would find no save
    /// queued yet and read as it was before the logout. <c>World.DeSpawnPlayerAsync</c> queues its
    /// save before its first await and clears the connection's character, so by the time it returns
    /// the save is in the chain waited on, and the despawn the close queues later finds nothing left
    /// to save. The wait for a kicked <em>different</em> character is taken here, after its despawn:
    /// the new select waits for every kicked character's logout save, not only its own.
    /// </para>
    /// <para>
    /// A connection part way through its own select holds no entity yet. Its select is cancelled,
    /// which is what stops the rest of its chain: every step checks <see cref="OwnsSelect" /> first
    /// and does nothing once the select it belongs to is gone. The one step it may already have
    /// running, a read or the select-time row write, cannot be called back, so it is handed to the
    /// new select to wait for as well. Past that step the kicked chain does nothing more, so nothing
    /// it started is still touching the database when the new select reads.
    /// </para>
    /// <para>
    /// Only connections of the same account are looked at. The world server's list includes
    /// connections that have already closed and whose despawn has not started, which is the same gap
    /// reached without a kick: a session that dropped a moment before this select. The auth server's
    /// online flag plays no part, since a dropped auth connection clears it while the world session
    /// is still up.
    /// </para>
    /// </remarks>
    /// <returns>What the new select must wait for, besides its own character's saves, before it reads.</returns>
    private List<Task> TakeOverFromOtherSessions(IWorldConnection connection, AccountId accountId, CharacterId id)
    {
        List<Task> waits = [];

        foreach (IWorldConnection other in worldServer.SessionsOf(accountId, connection))
        {
            ICharacter? held = other.Character ?? other.PendingSpawn?.Character;
            CharacterId? heldId = held is CharacterEntity { Data: { } row } ? row.Id : null;

            logger.LogInformation(
                "Character {CharacterId} selected for account {AccountId}; disconnecting another session of the account (holding {HeldCharacterId}, selecting {Selecting})",
                id.Value, accountId, heldId?.Value, other.SelectInProgress);

            // Before anything else: from here on none of its remaining select steps does anything.
            if (other.SelectInProgress)
                other.CancelSelect();

            if (_selectStepInFlight.TryGetValue(other, out Task? step) && !step.IsCompleted)
                waits.Add(step);

            // Queues the logout save and releases the character before it returns.
            _ = world.DeSpawnPlayerAsync(other);

            // Its own character's saves are waited on by FindAfterSavesAsync already.
            if (heldId is { } kicked && kicked != id)
                waits.Add(characterSaver.WhenIdle(kicked));

#pragma warning disable MA0045 // a tick-thread handler; the close finishes on its own
            GracefulShutdownHelper.NotifyAndClose(other,
                "Your account has been logged in from another location.", DisconnectReason.DuplicateLogin, logger);
#pragma warning restore MA0045
        }

        return waits;
    }

    /// <summary>
    /// Whether <paramref name="select" /> is still this connection's select. It stops being so when
    /// the select is cancelled (by its own give-up paths, by the stalled-select sweep, or by another
    /// session of the account kicking this one) and when the connection goes down. A step that finds
    /// it no longer owns its select does nothing: no write, no further read, no pending spawn.
    /// </summary>
    private bool OwnsSelect(IWorldConnection connection, long select)
    {
        if (connection.IsConnected && connection.SelectStartedTicks == select)
            return true;

        logger.LogInformation(
            "Abandoning a character select for account {AccountId}: it was cancelled, or its connection was kicked or closed",
            connection.AccountId);
        return false;
    }

    /// <summary>
    /// One step of the select chain: remembers <paramref name="task" /> as the step this connection
    /// has in flight, so a select that kicks it can wait for it, and runs <paramref name="next" />
    /// on the tick once it finishes, only if the connection still owns <paramref name="select" />.
    /// </summary>
    private void Step<T>(IWorldConnection connection, long select, Task<T> task, Action<T> next)
    {
        _selectStepInFlight.AddOrUpdate(connection, task);
        connection.EnqueueContinuation(task, result =>
        {
            if (OwnsSelect(connection, select))
                next(result);
        });
    }

    /// <summary>
    /// A relog builds a new entity from the database, while the previous session's despawn save may
    /// still be writing. Reading before it commits would load the inventory and money as they were
    /// before that save, and the next save would then write the stale state back over it. Every read
    /// of the select chain follows this one, so waiting here covers all of them. The same wait covers
    /// <paramref name="kickedWork" />: the logout saves of the other characters this select kicked,
    /// and any step a kicked select still had running.
    /// </summary>
    /// <remarks>
    /// Everything is waited on together, so each is bounded by the same <see cref="SaveWaitLimit" />.
    /// A wait that runs out reads nothing. Reading anyway would load the row and slots from before
    /// the save still running; that save would then commit, and the new session's first save (the
    /// one marking it online at spawn) would write the stale money and slots back over it. A kicked
    /// step that faulted has still finished, which is all that is waited for, so faults are ignored.
    /// </remarks>
    private async Task<(Character? Character, bool SaveStillRunning)> FindAfterSavesAsync(CharacterId id,
        AccountId accountId, IReadOnlyList<Task> kickedWork)
    {
        // Taken before the first await, on the tick, like the kicked characters' waits.
        Task idle = kickedWork.Count == 0
            ? characterSaver.WhenIdle(id)
            : Task.WhenAll([.. kickedWork, characterSaver.WhenIdle(id)]);

        if (!idle.IsCompleted)
        {
            await idle.WaitAsync(SaveWaitLimit, CancellationToken.None)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            if (!idle.IsCompleted)
            {
                logger.LogWarning(
                    "Character {CharacterId}, or a session of its account this select kicked, still had a save or read in flight after {Limit}; failing the select without reading it, so the client can retry",
                    id.Value, SaveWaitLimit);
                return (null, true);
            }
        }

        Character? character = await characterRepository.FindByIdAndAccountAsync(id, accountId, CancellationToken.None)
            .ConfigureAwait(false);
        return (character, false);
    }

    private void OnCharacterReceived(IWorldConnection connection, long select, Character? character)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnCharacterReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("CharacterId", character?.Id);

        if (character == null)
        {
            logger.LogWarning("Character not found for account {AccountId}", connection.AccountId);
            activity?.AddEvent(new ActivityEvent("CharacterNotFound"));
            connection.CancelSelect();
            return;
        }

        // Online is NOT set here. The row would then say the player is in the world for as long as
        // the client takes to load, and a disconnect inside that window has nothing in an instance
        // to write it back. World.SpawnInInstance sets it.
        character.Latency = (int)connection.Latency;

        ulong requiredExperience = world.Data.CharacterLevelExperiences.FirstOrDefault(c => c.Level == character.Level)
            ?.Experience ?? 0;

        ClassLevelStat? classLevelStat = world.Data.ClassLevelStats
            .FirstOrDefault(s => s.Class == character.Class && s.Level == character.Level);

        CharacterEntity entity = new(loggerFactory, character, regenConfig.Value)
        {
            Data = character,
            Position = new Vector3(character.X, character.Y, character.Z),
            Velocity = Vector3.zero,
            Orientation = new Vector3(0, character.Rotation, 0),
            EnteredWorld = DateTime.UtcNow,
            RequiredExperience = requiredExperience
        };

        entity.Stamina = classLevelStat?.Stamina ?? 0;
        entity.RegenStat = character.Class switch
        {
            CharacterClass.Wizard or CharacterClass.Healer => classLevelStat?.Intellect ?? 0,
            CharacterClass.Hunter => classLevelStat?.Agility ?? 0,
            _ => 0
        };

        entity.CurrentHealth = entity.Health;
        entity.CurrentPower = entity.Power;
        entity.PowerType = character.Class switch
        {
            CharacterClass.Warrior => PowerType.Fury,
            CharacterClass.Wizard or CharacterClass.Healer => PowerType.Mana,
            CharacterClass.Hunter => PowerType.Energy,
            _ => PowerType.None
        };

        // connection.Character is NOT assigned here, and is not assigned by this handler at all.
        // The entity is handed to the connection as a pending spawn once inventory and spells are
        // loaded; CharacterReadinessBarrier assigns it.

        MapTemplate? loadedTemplate = world.MapTemplates.FirstOrDefault(t => t.Id == (MapTemplateId)character.Map);
        if (loadedTemplate == null)
        {
            logger.LogError("MapTemplate {MapId} not found for character {CharacterId}", character.Map,
                character.Id);
            activity?.AddEvent(new ActivityEvent("MapTemplateNotFound"));
            connection.CancelSelect();
            return;
        }

        // Login MUST land in a town. The persisted Map column should already point at a town
        // because DeSpawnPlayerAsync redirects on logout — but a force-quit before the save
        // path runs (e.g. server crash, abrupt connection drop pre-DeSpawn) can leave the row
        // pointing at a procedural/dungeon map. Walk the back-portal chain to the nearest
        // town as the same resolver used by death + logout-while-dead. Persist forward so
        // the row gets corrected on next save.
        if (loadedTemplate.MapType != MapType.Town)
        {
            logger.LogWarning(
                "Character {CharacterId} ({Name}) persisted in non-town map {MapId}; redirecting to nearest town",
                character.Id, character.Name, character.Map);
            Step(connection, select,
                respawnTargetResolver.ResolveTownAsync(loadedTemplate.Id, CancellationToken.None),
                townMapId =>
                {
                    var townTpl = world.MapTemplates.FirstOrDefault(t => t.Id == townMapId);
                    if (townTpl == null)
                    {
                        logger.LogError("Resolved town map {TownMapId} not found in MapTemplates",
                            townMapId.Value);
                        connection.CancelSelect();
                        return;
                    }
                    character.Map = townMapId.Value;
                    character.X = townTpl.DefaultSpawnX;
                    character.Y = townTpl.DefaultSpawnY;
                    character.Z = townTpl.DefaultSpawnZ;
                    entity.Position = new Vector3(character.X, character.Y, character.Z);
                    EnterTownInstance(connection, select, entity, townTpl);
                });
            _parentActivity = activity;
            return;
        }

        EnterTownInstance(connection, select, entity, loadedTemplate);
        _parentActivity = activity;
    }

    private void EnterTownInstance(IWorldConnection connection, long select, CharacterEntity entity,
        MapTemplate townTemplate)
    {
        Step(connection, select,
            world.InstanceRegistry.GetOrCreateTownInstanceAsync(townTemplate.Id, townTemplate.MaxPlayers ?? 30),
            mapInstance => OnInstanceObtained(connection, select, entity, townTemplate, mapInstance));
    }

    private void OnInstanceObtained(IWorldConnection connection, long select, CharacterEntity entity,
        MapTemplate townTemplate, IMapInstance instance)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnInstanceObtained),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("CharacterId", entity.Data?.Id);
        activity?.SetTag("InstanceId", instance.InstanceId);

        entity.InstanceId = instance.InstanceId;

        Character character = entity.Data!;

        // Towns spawn the player at the chunk-layout entry — overriding the persisted
        // Character.X/Y/Z. The persisted coords are not authoritative for hubs (they were
        // baked under the old world.bin pipeline and would put returning players in random
        // spots inside the new chunk-composed town). Procedural maps still use the
        // persisted coords (set by EnterMapHandler when the player traversed in).
        float spawnX = character.X;
        float spawnY = character.Y;
        float spawnZ = character.Z;
        if (instance is MapInstance townMi && townMi.Layout is { } townLayout
            && townTemplate.MapType == MapType.Town)
        {
            spawnX = townLayout.EntrySpawnWorldPos.x;
            spawnY = townLayout.EntrySpawnWorldPos.y;
            spawnZ = townLayout.EntrySpawnWorldPos.z;
            entity.Position = new Vector3(spawnX, spawnY, spawnZ);
        }

        CharacterInfo characterInfo = new()
        {
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            Class = (ushort)character.Class,
            X = spawnX,
            Y = spawnY,
            Z = spawnZ,
            Orientation = character.Rotation,
            Experience = character.Experience,
            RequiredExperience = entity.RequiredExperience,
            MovementSpeed = entity.GetMovementSpeed(),
        };

        MapInfo mapInfo = new()
        {
            MapId = character.Map,
            InstanceId = instance.InstanceId,
            Name = townTemplate.Description,
            Description = townTemplate.Description
        };

        connection.Send(SCharacterSelectedPacket.Create(characterInfo, mapInfo, connection.CryptoSession.Encrypt));

        // Send chunk layout so the client can compose the stitched map
        // and bake its local navmesh. Town + normal both flow through
        // ChunkLayoutInstanceFactory now, so any MapInstance with a Layout qualifies.
        if (instance is MapInstance layoutMi && layoutMi.Layout is { } layout)
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
                character.Map,
                layout.CellSize,
                dtos,
                layout.EntrySpawnWorldPos,
                portalDtos,
                connection.CryptoSession.Encrypt));
        }

        Step(connection, select, characterRepository.UpdateAsync(character, CancellationToken.None), _ =>
        {
            Step(connection, select, characterInventoryRepository.GetByCharacterIdAsync(character.Id, CancellationToken.None),
                items => OnInventoryReceived(connection, select, entity, instance, character, items));
        });

        _parentActivity = activity;
    }

    private void OnInventoryReceived(IWorldConnection connection, long select, CharacterEntity entity,
        IMapInstance instance, Character character, IReadOnlyCollection<CharacterInventory> items)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnInventoryReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("CharacterId", character.Id);

        // The rows say where the items sit; the instances say what they are. Both live in the
        // Character database but are read as two queries. Nothing can be loaded or sent until
        // both are in hand.
        //
        // Without the templates: login reads only the instance's own columns, and the client
        // resolves template ids against the vendored item catalog. Joining 41 columns per carried
        // item would load rows nothing here reads.
        Step(connection, select,
            itemInstanceRepository.GetByCharacterIdAsync(character.Id, CancellationToken.None),
            instances => OnItemInstancesReceived(connection, select, entity, instance, character, items, instances));

        _parentActivity = activity;
    }

    private void OnItemInstancesReceived(IWorldConnection connection, long select, CharacterEntity entity,
        IMapInstance instance, Character character,
        IReadOnlyCollection<CharacterInventory> rows, IReadOnlyCollection<ItemInstance> instances)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnItemInstancesReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("CharacterId", character.Id);

        IReadOnlyDictionary<InventoryType, List<InventoryItem>> assembled =
            InventoryAssembler.Assemble(rows, instances, logger);

        entity[InventoryType.Equipment].Load(assembled[InventoryType.Equipment]);
        entity[InventoryType.Bag].Load(assembled[InventoryType.Bag]);
        entity[InventoryType.Bank].Load(assembled[InventoryType.Bank]);

        // The bank is loaded but not sent: opening it is a separate interaction, and a client
        // told about items it has no way to show would have to decide what to do with them.
        ItemSlotDto[] carried =
        [
            .. ToDtos(InventoryType.Equipment, entity[InventoryType.Equipment].Items),
            .. ToDtos(InventoryType.Bag, entity[InventoryType.Bag].Items),
        ];

        connection.Send(SInventorySnapshotPacket.Create(carried, character.Money, connection.CryptoSession.Encrypt));

        Step(connection, select, characterAbilityRepository.GetCharacterAbilitiesAsync(character.Id, CancellationToken.None),
            spells => OnSpellsReceived(connection, entity, instance, spells));
        _parentActivity = activity;
    }

    private static IEnumerable<ItemSlotDto> ToDtos(InventoryType container, IReadOnlyCollection<InventoryItem> items)
        => items.Select(item => ItemSlotDtoMapper.ToDto(container, item));

    private void OnSpellsReceived(IWorldConnection connection, CharacterEntity entity, IMapInstance instance,
        IReadOnlyCollection<CharacterAbility> spells)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnSpellsReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("Spells.Count", spells.Count);

        List<GameAbility> gameAbilities = [];

        foreach (CharacterAbility characterAbility in spells)
        {
            AbilityTemplate? template = world.Data.AbilityTemplates.FirstOrDefault(sp => sp.Id == characterAbility.AbilityId);
            if (template == null)
            {
                logger.LogWarning("Spell template not found for spell {AbilityId}", characterAbility.AbilityId);
                activity?.AddEvent(new ActivityEvent("SpellTemplateNotFound"));
                continue;
            }

            GameAbility gameAbility = new()
            {
                AbilityId = characterAbility.AbilityId,
                Metadata = new AbilityMetadata
                {
                    Name = template.Name,
                    Cooldown = (float)template.Cooldown / 1000,
                    CastTime = (float)template.CastTime / 1000,
                    Cost = template.Cost,
                    Range = template.Range,
                    Effects = template.Effects,
                    EffectValue = template.EffectValue,
                    ScriptName = template.SpellScript,
                    ThreatMultiplier = template.ThreatMultiplier,
                    HealThreatPerHp = template.HealThreatPerHp,
                    TauntDurationMs = template.TauntDurationMs,
                    Flags = template.Flags,
                    AnimationId = template.AnimationId
                },
                CastTimeTimer = (float)template.CastTime / 1000,
                CooldownTimer = characterAbility.Cooldown
            };

            gameAbilities.Add(gameAbility);
        }

        entity.Spells.Load(gameAbilities);

        AbilityInfo[] abilityInfos = gameAbilities.Select(s => new AbilityInfo
        {
            AbilityId = s.AbilityId,
            Name = s.Metadata.Name,
            Cooldown = s.Metadata.Cooldown,
            CastTime = s.Metadata.CastTime,
            Cost = s.Metadata.Cost,
            Range = (ushort)s.Metadata.Range
        }).ToArray();

        connection.Send(SCharacterAbilitiesPacket.Create(abilityInfos, connection.CryptoSession.Encrypt));

        // All data loaded, but the client has not composed the map yet. The entity is held as a
        // pending spawn instead of being assigned and spawned here, so nothing on the tick sees a
        // character whose client is still loading. CharacterLoadedHandler releases it when the
        // client reports in; WorldServer's tick releases it anyway once the barrier expires.
        connection.SetPendingSpawn(entity, instance, DateTime.UtcNow.Ticks);

        logger.LogInformation(
            "Character {CharacterName} selected for account {AccountId}; awaiting the client's load report",
            entity.Data?.Name, connection.AccountId);
    }
}
