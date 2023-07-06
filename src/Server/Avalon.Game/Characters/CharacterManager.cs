using System.Numerics;
using Avalon.Database.Characters;
using Avalon.Network;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    private const int MaxCharactersPerAccount = 5;
    
    public async Task HandleCharacterListPacket(IRemoteSource source, CCharacterListPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }

        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", packet.AccountId);
            return;
        }

        var characters = await _databaseManager.Characters.Character.QueryByAccountAsync(packet.AccountId);

        var characterInfo = characters.Select(character => new CharacterInfo
        {
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            Class = character.Class
        }).ToArray();
        
        var responsePacket = SCharacterListPacket.Create(session.AccountId, characterInfo.Length, MaxCharactersPerAccount, characterInfo);
        
        await session.SendAsync(responsePacket);
    }

    public async Task HandleCharacterCreatePacket(IRemoteSource source, CCharacterCreatePacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", packet.AccountId);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.AlreadyInGame));
            return;
        }
        
        var characters = await _databaseManager.Characters.Character.QueryByAccountAsync(packet.AccountId);
        
        if (characters.Count() == MaxCharactersPerAccount || characters.Count() + 1 > MaxCharactersPerAccount)
        {
            _logger.LogDebug("Account {AccountId} already has {CharacterCount} characters", packet.AccountId, characters.Count());
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.MaxCharactersReached));
            return;
        }
        
        var duplicateCharacter = await _databaseManager.Characters.Character.QueryByNameAsync(packet.Name);
        if (duplicateCharacter != null)
        {
            _logger.LogDebug("Character {Name} already exists", packet.Name);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.NameAlreadyExists));
            return;
        }
        
        var character = new Character
        {
            Account = packet.AccountId,
            Name = packet.Name,
            Level = 1,
            Class = packet.Class,
            PositionX = 326, // TODO: Get starting position from starting map
            PositionY = 1450 // TODO: Get starting position from starting map
        };

        if (!await _databaseManager.Characters.Character.InsertAsync(character))
        {
            _logger.LogWarning("Failed to create character {Name}", packet.Name);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.InternalDatabaseError));
            return;
        }
        
        _logger.LogInformation("Character {Name} created for account {AccountId}", packet.Name, packet.AccountId);
        await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.Success));
    }
    
    public async Task HandleCharacterSelectedPacket(IRemoteSource source, CCharacterSelectedPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }

        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", packet.AccountId);
            return;
        }

        var character = await _databaseManager.Characters.Character.QueryByIdAndAccountAsync(packet.CharacterId, packet.AccountId);
        
        if (character == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", packet.CharacterId, packet.AccountId);
            return;
        }
        
        // Try to find the map instance for this character
        var mapInstance = _avalonMapManager.GetInstance(character.Map, Guid.Parse(character.InstanceId));
        if (mapInstance == null)
        {
            // Specific map instance not found, try to find a other instance of the same map
            mapInstance = _avalonMapManager.GetInstance(character.Map, character.Id);
            if (mapInstance == null)
            {
                // No instance found, create a new one and add the character to it
                mapInstance = _avalonMapManager.GenerateInstance(character.Map);
                mapInstance.AddCharacter(character.Id);
            }
        }
        
        // Update the current map instance
        character.InstanceId = mapInstance.InstanceId.ToString();

        character.Movement = new CharacterMovement
        {
            Position = new Vector2(character.PositionX, character.PositionY),
            Velocity = Vector2.Zero
        };
        
        character.Online = true;

        await session.SendAsync(SCharacterSelectedPacket.Create(packet.AccountId, new CharacterInfo
        {
            CharacterId = character.Id,
            Name = character.Name,
            Level = character.Level,
            Class = character.Class,
            X = character.PositionX,
            Y = character.PositionY
        },
        new MapInfo
        {
            MapId = mapInstance.MapId,
            InstanceId = mapInstance.InstanceId,
            Name = mapInstance.Name,
            Atlas = mapInstance.Atlas,
            Directory = mapInstance.Directory,
        }));

        // save the character to the database
        await _databaseManager.Characters.Character.UpdateAsync(character);

        session.Character = character;
        
        _logger.LogInformation("Character {CharacterId} logged in for account {AccountId} at {Position}", character.Name, packet.AccountId, character.Movement);

        // Send to everyone else, that this player is connected
        await BroadcastToOthersInInstance(session.AccountId, SPlayerConnectedPacket.Create(session.AccountId, session.Character.Id, session.Character.Name), mapInstance.InstanceId.ToString());
    }

    public async Task HandleCharacterDeletePacket(IRemoteSource source, CCharacterDeletePacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", packet.AccountId);
            await session.SendAsync(SCharacterDeletedPacket.Create(packet.AccountId, SCharacterDeletedResult.InGame));
            return;
        }
        
        var character = await _databaseManager.Characters.Character.QueryByIdAndAccountAsync(packet.CharacterId, packet.AccountId);
        if (character == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", packet.CharacterId, packet.AccountId);
            await session.SendAsync(SCharacterDeletedPacket.Create(packet.AccountId, SCharacterDeletedResult.InternalError));
            return;
        }
        
        if (!await _databaseManager.Characters.Character.DeleteAsync(character.Id, character.Account))
        {
            _logger.LogWarning("Failed to delete character {CharacterId} for account {AccountId}", packet.CharacterId, packet.AccountId);
            await session.SendAsync(SCharacterDeletedPacket.Create(packet.AccountId, SCharacterDeletedResult.InternalError));
            return;
        }
        
        _logger.LogInformation("Character {CharacterId} deleted for account {AccountId}", packet.CharacterId, packet.AccountId);
        await session.SendAsync(SCharacterDeletedPacket.Create(packet.AccountId, SCharacterDeletedResult.Success));
    }

    public async Task HandleCharacterLoadedPacket(IRemoteSource source, CCharacterLoadedPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        if (!session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is not in game", packet.AccountId);
            return;
        }
        
        var sessions = _connectionManager.GetSessions();
        
        // Send to this player, that everyone else is connected
        foreach (var otherSession in sessions.Values)
        {
            if (otherSession.AccountId == session.AccountId || !otherSession.InGame || otherSession.Character!.InstanceId != session.Character!.InstanceId)
            {
                continue;
            }
            
            await session.SendAsync(SPlayerConnectedPacket.Create(otherSession.AccountId, otherSession.Character!.Id, otherSession.Character.Name));
        }
    }
}
