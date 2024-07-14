using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Database.Repositories;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World.Handlers;

public class CharacterCreateHandler(
    ILogger<CharacterCreateHandler> logger,
    ICharacterRepository characterRepository,
    ICharacterCreateInfoRepository createInfoRepository,
    IClassLevelStatRepository classLevelStatRepository,
    ICreatureStatsRepository characterStatsRepository,
    IWorld world)
    : IWorldPacketHandler<CCharacterCreatePacket>
{

    public async Task ExecuteAsync(WorldPacketContext<CCharacterCreatePacket> ctx, CancellationToken token = default)
    {
        if (ctx.Connection.AccountId == null)
        {
            logger.LogWarning("Connection tried to create a character without being authenticated");
            ctx.Connection.Close();
            return;       
        }

        if (ctx.Connection.CharacterId != null)
        {
            logger.LogWarning("Connection tried to create a character while already having a character selected");
            ctx.Connection.Close();
            return;
        }
        
        var encrypt = ctx.Connection.CryptoSession.Encrypt;
        
        var characters = await characterRepository.FindByAccountAsync(ctx.Connection.AccountId);
        
        var currentCharacterCount = characters.Count;
        
        if (currentCharacterCount == world.Configuration.MaxCharactersPerAccount || currentCharacterCount + 1 > world.Configuration.MaxCharactersPerAccount)
        {
            logger.LogDebug("Account {AccountId} already has {CharacterCount} characters", ctx.Connection.AccountId, currentCharacterCount);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.MaxCharactersReached, encrypt));
            return;
        }
        
        var duplicateCharacter = await characterRepository.FindByNameAsync(ctx.Packet.Name);
        if (duplicateCharacter != null)
        {
            logger.LogDebug("Character {Name} already exists", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameAlreadyExists, encrypt));
            return;
        }
        
        if (ctx.Packet.Name.Length < 3)
        {
            logger.LogDebug("Character name {Name} is too short", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameTooShort, encrypt));
            return;
        }
        
        if (ctx.Packet.Name.Length > 12)
        {
            logger.LogDebug("Character name {Name} is too long", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.NameTooLong, encrypt));
            return;
        }
        
        var @class = (CharacterClass) ctx.Packet.Class;
        
        var createInfo = await createInfoRepository.GetByClassAsync(@class);
        if (createInfo == null)
        {
            logger.LogWarning("Character class {Class} does not have a creation info", ctx.Packet.Class);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, encrypt));
            return;
        }
        
        var classLevelStat = await classLevelStatRepository.GetByLevelAsync(@class, 1);
        if (classLevelStat == null)
        {
            logger.LogWarning("Character class {Class} does not have a level 1 stat info", ctx.Packet.Class);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, encrypt));
            return;
        }
        
        try
        {
        
            var character = new Character
            {
                AccountId = ctx.Connection.AccountId!.Value,
                Name = ctx.Packet.Name,
                Level = classLevelStat.Level,
                Class = createInfo.Class,
                X = createInfo.X,
                Y = createInfo.Y,
                Z = createInfo.Z,
                Rotation = createInfo.Rotation,
                Map = createInfo.Map,
                CreationDate = DateTime.UtcNow,
                Health = (int) CharacterStats.GetBaseHp(@class, classLevelStat.Stamina, classLevelStat.Level),
                Power1 = (int) CharacterStats.GetBasePower(@class, classLevelStat.Intellect, classLevelStat.Agility, classLevelStat.Level),
                Power2 = 0,
                Experience = 0,
            };
            
            character = await characterRepository.CreateAsync(character);
            
            var characterStats = new CharacterStats
            {
                Character = character,
                CharacterId = character.Id,
                MaxHealth = CharacterStats.GetBaseHp(@class, classLevelStat.Stamina, classLevelStat.Level),
                MaxPower1 = CharacterStats.GetBasePower(@class, classLevelStat.Intellect, classLevelStat.Agility, classLevelStat.Level),
                MaxPower2 = 0,
                Stamina = classLevelStat.Stamina,
                Strength = classLevelStat.Strength,
                Agility = classLevelStat.Agility,
                Intellect = classLevelStat.Intellect,
                Armor = 0,
                BlockPct = CharacterStats.GetBaseBlockPercent(@class),
                DodgePct = CharacterStats.GetBaseDodgePercent(@class),
                CritPct = CharacterStats.GetBaseCritPercent(@class),
                AttackDamage = CharacterStats.GetBaseAttackDamage(@class, classLevelStat.Strength, classLevelStat.Agility),
                AbilityDamage = CharacterStats.GetBaseAbilityDamage(@class, classLevelStat.Intellect),
            };
            
            await characterStatsRepository.CreateAsync(characterStats);
            
            logger.LogInformation("Character {Name} created for account {AccountId}", ctx.Packet.Name, encrypt);
            
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.Success, encrypt));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to create character {Name}", ctx.Packet.Name);
            ctx.Connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.InternalDatabaseError, encrypt));
        }
    }
}
