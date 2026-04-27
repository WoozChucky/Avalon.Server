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
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Spells;
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
    IWorld world,
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

        MapTemplate? townTemplate = world.MapTemplates.FirstOrDefault(t => t.Id == (MapTemplateId)character.Map);
        if (townTemplate == null)
        {
            logger.LogError("MapTemplate {MapId} not found for character {CharacterId}", character.Map,
                character.Id);
            activity?.AddEvent(new ActivityEvent("MapTemplateNotFound"));
            return;
        }

        connection.EnqueueContinuation(
            world.InstanceRegistry.GetOrCreateTownInstanceAsync(townTemplate.Id, townTemplate.MaxPlayers ?? 30),
            mapInstance => OnInstanceObtained(connection, entity, townTemplate, mapInstance));

        _parentActivity = activity;
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

        CharacterInfo characterInfo = new()
        {
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            Class = (ushort)character.Class,
            X = character.X,
            Y = character.Y,
            Z = character.Z,
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
