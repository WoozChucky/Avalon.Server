using Avalon.World.Public;
using System.Diagnostics;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Spells;
using Avalon.World.Respawn;
using Avalon.World.Spells;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_SELECTED)]
public class CharacterSelectHandler(
    ILogger<CharacterSelectHandler> logger,
    ILoggerFactory loggerFactory,
    ICharacterRepository characterRepository,
    ICharacterInventoryRepository characterInventoryRepository,
    ICharacterSpellRepository characterSpellRepository,
    IChunkLibrary chunkLibrary,
    IWorld world,
    IRespawnTargetResolver respawnTargetResolver,
    IOptions<RegenConfiguration> regenConfig) : WorldPacketHandler<CCharacterSelectedPacket>
{
    private Activity? _parentActivity;

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

        if (connection.Character != null)
        {
            logger.LogWarning("Connection tried to select a character list while already having a character selected");
            activity?.AddEvent(new ActivityEvent("DuplicateSelectionAttempt"));
            connection.Close();
            return;
        }

        connection.EnqueueContinuation(
            characterRepository.FindByIdAndAccountAsync(packet.CharacterId, connection.AccountId),
            character => { OnCharacterReceived(connection, character); });

        _parentActivity = activity;
    }

    private void OnCharacterReceived(IWorldConnection connection, Character? character)
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
            return;
        }

        character.Online = true;
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

        // connection.Character is NOT assigned here. Assignment is deferred to OnSpellsReceived
        // so the tick loop only sees a fully-initialized entity (inventory + spells loaded).

        MapTemplate? loadedTemplate = world.MapTemplates.FirstOrDefault(t => t.Id == (MapTemplateId)character.Map);
        if (loadedTemplate == null)
        {
            logger.LogError("MapTemplate {MapId} not found for character {CharacterId}", character.Map,
                character.Id);
            activity?.AddEvent(new ActivityEvent("MapTemplateNotFound"));
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
            connection.EnqueueContinuation(
                respawnTargetResolver.ResolveTownAsync(loadedTemplate.Id, CancellationToken.None),
                townMapId =>
                {
                    var townTpl = world.MapTemplates.FirstOrDefault(t => t.Id == townMapId);
                    if (townTpl == null)
                    {
                        logger.LogError("Resolved town map {TownMapId} not found in MapTemplates",
                            townMapId.Value);
                        return;
                    }
                    character.Map = townMapId.Value;
                    character.X = townTpl.DefaultSpawnX;
                    character.Y = townTpl.DefaultSpawnY;
                    character.Z = townTpl.DefaultSpawnZ;
                    entity.Position = new Vector3(character.X, character.Y, character.Z);
                    EnterTownInstance(connection, entity, townTpl);
                });
            _parentActivity = activity;
            return;
        }

        EnterTownInstance(connection, entity, loadedTemplate);
        _parentActivity = activity;
    }

    private void EnterTownInstance(IWorldConnection connection, CharacterEntity entity, MapTemplate townTemplate)
    {
        connection.EnqueueContinuation(
            world.InstanceRegistry.GetOrCreateTownInstanceAsync(townTemplate.Id, townTemplate.MaxPlayers ?? 30),
            mapInstance => OnInstanceObtained(connection, entity, townTemplate, mapInstance));
    }

    private void OnInstanceObtained(IWorldConnection connection, CharacterEntity entity, MapTemplate townTemplate,
        IMapInstance instance)
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

        connection.EnqueueContinuation(characterRepository.UpdateAsync(character, CancellationToken.None), _ =>
        {
            connection.EnqueueContinuation(characterInventoryRepository.GetByCharacterIdAsync(character.Id, CancellationToken.None),
                items => OnInventoryReceived(connection, entity, instance, character, items));
        });

        _parentActivity = activity;
    }

    private void OnInventoryReceived(IWorldConnection connection, CharacterEntity entity, IMapInstance instance,
        Character character, IReadOnlyCollection<CharacterInventory> items)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnInventoryReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("CharacterId", character.Id);

        entity[InventoryType.Equipment].Load(items.Where(i => i.Container == InventoryType.Equipment).ToList());
        entity[InventoryType.Bag].Load(items.Where(i => i.Container == InventoryType.Bag).ToList());
        entity[InventoryType.Bank].Load(items.Where(i => i.Container == InventoryType.Bank).ToList());

        //TODO: Send inventory to the client

        connection.EnqueueContinuation(characterSpellRepository.GetCharacterSpellsAsync(character.Id, CancellationToken.None),
            spells => OnSpellsReceived(connection, entity, instance, spells));
        _parentActivity = activity;
    }

    private void OnSpellsReceived(IWorldConnection connection, CharacterEntity entity, IMapInstance instance,
        IReadOnlyCollection<CharacterSpell> spells)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnSpellsReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag(nameof(connection.AccountId), connection.AccountId);
        activity?.SetTag("Spells.Count", spells.Count);

        List<GameSpell> gameSpells = [];

        foreach (CharacterSpell characterSpell in spells)
        {
            SpellTemplate? template = world.Data.SpellTemplates.FirstOrDefault(sp => sp.Id == characterSpell.SpellId);
            if (template == null)
            {
                logger.LogWarning("Spell template not found for spell {SpellId}", characterSpell.SpellId);
                activity?.AddEvent(new ActivityEvent("SpellTemplateNotFound"));
                continue;
            }

            GameSpell gameSpell = new()
            {
                SpellId = characterSpell.SpellId,
                Metadata = new SpellMetadata
                {
                    Name = template.Name,
                    Cooldown = (float)template.Cooldown / 1000,
                    CastTime = (float)template.CastTime / 1000,
                    Cost = template.Cost,
                    Range = template.Range,
                    Effects = template.Effects,
                    EffectValue = template.EffectValue,
                    ScriptName = template.SpellScript
                },
                CastTimeTimer = (float)template.CastTime / 1000,
                CooldownTimer = characterSpell.Cooldown
            };

            gameSpells.Add(gameSpell);
        }

        entity.Spells.Load(gameSpells);

        SpellInfo[] spellInfos = gameSpells.Select(s => new SpellInfo
        {
            SpellId = s.SpellId,
            Name = s.Metadata.Name,
            Cooldown = s.Metadata.Cooldown,
            CastTime = s.Metadata.CastTime,
            Cost = s.Metadata.Cost,
            Range = (ushort)s.Metadata.Range
        }).ToArray();

        connection.Send(SCharacterSpellsPacket.Create(spellInfos, connection.CryptoSession.Encrypt));

        // All data loaded: assign character and spawn atomically on the tick thread.
        // After this point the entity is visible to MapInstance.Update.
        connection.Character = entity;
        try
        {
            world.SpawnInInstance(connection, instance);
        }
        catch (Exception e)
        {
            connection.Character = null;
            logger.LogError(e, "Error while spawning player {CharacterId}", entity.Data?.Id);
            return;
        }

        logger.LogInformation("Character {CharacterName} logged in for account {AccountId} at {Position}",
            entity.Data?.Name, connection.AccountId, entity.Position);
    }
}
