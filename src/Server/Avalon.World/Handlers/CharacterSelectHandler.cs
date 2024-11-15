using System.Diagnostics;
using Avalon.Common.Mathematics;
using Avalon.Common.Telemetry;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World.Entities;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Avalon.World.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Handlers;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_SELECTED)]
public class CharacterSelectHandler(
    ILogger<CharacterSelectHandler> logger,
    ILoggerFactory loggerFactory,
    ICharacterRepository characterRepository,
    ICharacterInventoryRepository characterInventoryRepository,
    ICharacterSpellRepository characterSpellRepository,
    IWorld world) : WorldPacketHandler<CCharacterSelectedPacket>
{
    private Activity? _parentActivity;

    public override void Execute(WorldConnection connection, CCharacterSelectedPacket packet)
    {
        using Activity? activity =
            DiagnosticsConfig.World.Source.StartActivity(nameof(CharacterSelectHandler), ActivityKind.Server);
        activity?.SetTag("connection.accountId", connection.AccountId);
        activity?.SetTag("characterId", packet.CharacterId);

        if (connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to select a character list without being authenticated");
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

        connection.AddQueryCallback(
            characterRepository.FindByIdAndAccountAsync(packet.CharacterId, connection.AccountId),
            character => { OnCharacterReceived(connection, character); });

        _parentActivity = activity;
    }

    private void OnCharacterReceived(WorldConnection connection, Character? character)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnCharacterReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag("connection.accountId", connection.AccountId);
        activity?.SetTag("characterId", character?.Id);

        if (character == null)
        {
            logger.LogWarning("Character not found for account {AccountId}", connection.AccountId);
            activity?.AddEvent(new ActivityEvent("CharacterNotFound"));
            return;
        }

        //TODO: Implement when instances are a thing
        character.InstanceId = Guid.NewGuid().ToString();
        character.Online = true;
        character.Latency = (int)connection.Latency;

        ulong requiredExperience = world.Data.CharacterLevelExperiences.FirstOrDefault(c => c.Level == character.Level)
            ?.Experience ?? 0;

        CharacterEntity entity = new(loggerFactory, connection, character)
        {
            Data = character,
            Position = new Vector3(character.X, character.Y, character.Z),
            Velocity = Vector3.zero,
            Orientation = new Vector3(0, character.Rotation, 0),
            EnteredWorld = DateTime.UtcNow,
            RequiredExperience = requiredExperience
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

        connection.Character = entity;

        try
        {
            // TODO: Spawn in the world
            world.SpawnPlayer(connection);
        }
        catch (Exception e)
        {
            connection.Character = null;
            logger.LogError(e, "Error while spawning player {CharacterId}", character.Id);
            activity?.AddEvent(new ActivityEvent("SpawnPlayerError"));
            return;
        }

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
            Running = character.Running,
            Experience = character.Experience,
            RequiredExperience = entity.RequiredExperience
        };

        MapInfo mapInfo = new()
        {
            MapId = character.Map,
            InstanceId = Guid.Parse(character.InstanceId),
            Name = "Test Map",
            Description = "Test Map Description"
        };

        connection.Send(SCharacterSelectedPacket.Create(characterInfo, mapInfo, connection.CryptoSession.Encrypt));

        connection.AddQueryCallback(characterRepository.UpdateAsync(character), _ =>
        {
            logger.LogInformation("Character {CharacterId} logged in for account {AccountId} at {Position}",
                character.Name, connection.AccountId, connection.Character.Position);

            connection.AddQueryCallback(characterInventoryRepository.GetByCharacterIdAsync(character.Id),
                items => { OnInventoryReceived(connection, character, items); });
        });
        _parentActivity = activity;
    }

    private void OnInventoryReceived(WorldConnection connection, Character character,
        IReadOnlyCollection<CharacterInventory> items)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnInventoryReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag("connection.accountId", connection.AccountId);
        activity?.SetTag("characterId", character.Id);

        ICharacter? entity = connection.Character;
        if (entity == null)
        {
            logger.LogWarning("Character entity is null for account {AccountId}", connection.AccountId);
            activity?.AddEvent(new ActivityEvent("CharacterEntityNull"));
            return;
        }

        entity[InventoryType.Equipment].Load(items.Where(i => i.Container == InventoryType.Equipment).ToList());
        entity[InventoryType.Bag].Load(items.Where(i => i.Container == InventoryType.Bag).ToList());
        entity[InventoryType.Bank].Load(items.Where(i => i.Container == InventoryType.Bank).ToList());

        //TODO: Send inventory to the client

        connection.AddQueryCallback(characterSpellRepository.GetCharacterSpellsAsync(character.Id),
            spells => { OnSpellsReceived(connection, spells); });
        _parentActivity = activity;
    }

    private void OnSpellsReceived(WorldConnection connection, IReadOnlyCollection<CharacterSpell> spells)
    {
        using Activity? activity = DiagnosticsConfig.World.Source.StartActivity(nameof(OnSpellsReceived),
            ActivityKind.Internal,
            _parentActivity?.Context ?? default);
        activity?.SetTag("connection.accountId", connection.AccountId);
        activity?.SetTag("spells.count", spells.Count);

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

        connection.Character!.Spells.Load(gameSpells);

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
    }
}
