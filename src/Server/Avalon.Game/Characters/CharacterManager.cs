using System.Numerics;
using Avalon.Database.Characters;
using Avalon.Domain.Characters;
using Avalon.Infrastructure;
using Avalon.Network;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public partial class AvalonGame
{
    // TODO: Move this to a config file
    private const int MaxCharactersPerAccount = 5;
    
    public async Task HandleCharacterListPacket(IRemoteSource source, CCharacterListPacket packet)
    {
        var session = _sessionManager.GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        var sessionLock = _sessionManager.GetSessionLock(session);
        
        await sessionLock.WaitAsync();
        
        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", session.AccountId);
            return;
        }
        
        sessionLock.Release();

        var characters = await _databaseManager.Characters.Character.FindByAccountAsync(session.AccountId);

        var characterInfo = characters.Select<Character, CharacterInfo>(
            character => new CharacterInfo
        {
            CharacterId = character.Id!.Value,
            Name = character.Name,
            Level = character.Level,
            Class = character.Class
        }).ToArray();
        
        var responsePacket = SCharacterListPacket.Create(
            session.AccountId, 
            characterInfo.Length, 
            MaxCharactersPerAccount, 
            characterInfo, 
            session.Encrypt
        );
        
        await session.SendAsync(responsePacket);
    }

    public async Task HandleCharacterCreatePacket(IRemoteSource source, CCharacterCreatePacket packet)
    {
        var session = _sessionManager.GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        var sessionLock = _sessionManager.GetSessionLock(session);
        
        await sessionLock.WaitAsync();
        
        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", packet.AccountId);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.AlreadyInGame, session.Encrypt));
            return;
        }
        
        sessionLock.Release();
        
        var characters = await _databaseManager.Characters.Character.FindByAccountAsync(packet.AccountId);
        
        var currentCharacterCount = characters.Count();
        
        if (currentCharacterCount == MaxCharactersPerAccount || currentCharacterCount + 1 > MaxCharactersPerAccount)
        {
            _logger.LogDebug("Account {AccountId} already has {CharacterCount} characters", packet.AccountId, currentCharacterCount);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.MaxCharactersReached, session.Encrypt));
            return;
        }
        
        var duplicateCharacter = await _databaseManager.Characters.Character.FindByNameAsync(packet.Name);
        if (duplicateCharacter != null)
        {
            _logger.LogDebug("Character {Name} already exists", packet.Name);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.NameAlreadyExists, session.Encrypt));
            return;
        }
        
        if (packet.Name.Length < 3)
        {
            _logger.LogDebug("Character name {Name} is too short", packet.Name);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.NameTooShort, session.Encrypt));
            return;
        }
        
        if (packet.Name.Length > 12)
        {
            _logger.LogDebug("Character name {Name} is too long", packet.Name);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.NameTooLong, session.Encrypt));
            return;
        }
        
        // TODO: Check if class is valid (create an enum for this)
        
        var character = new Character
        {
            Account = session.AccountId,
            Name = packet.Name, // TODO: Sanitize name
            Level = 1,
            Class = packet.Class,
            PositionX = 326, // TODO: Get starting position from starting map
            PositionY = 1450 // TODO: Get starting position from starting map
        };

        try
        {
            await _databaseManager.Characters.Character.SaveAsync(character);
            _logger.LogInformation("Character {Name} created for account {AccountId}", packet.Name, packet.AccountId);
            
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.Success, session.Encrypt));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to create character {Name}", packet.Name);
            await session.SendAsync(SCharacterCreatedPacket.Create(packet.AccountId, SCharacterCreateResult.InternalDatabaseError, session.Encrypt));
        }
    }
    
    public async Task HandleCharacterSelectedPacket(IRemoteSource source, CCharacterSelectedPacket packet)
    {
        var session = _sessionManager.GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {Address}", source.RemoteAddress);
            return;
        }
        
        var sessionLock = _sessionManager.GetSessionLock(session);

        await sessionLock.WaitAsync();

        try
        {
            if (session.InGame)
            {
                _logger.LogWarning("Session {AccountId} is already in game", session.AccountId);
                return;
            }

            var character = await _databaseManager.Characters.Character.FindByIdAndAccountAsync(packet.CharacterId, session.AccountId);
            
            if (character == null)
            {
                _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", packet.CharacterId, session.AccountId);
                return;
            }
            
            // Try to find the map instance for this character
            var mapInstance = _mapManager.GetInstance(character.Map, Guid.Parse(character.InstanceId!));
            if (mapInstance == null)
            {
                // Specific map instance not found, try to find a other instance of the same map
                // If no instance found, (internally will create a new one) add the session to it
                mapInstance = _mapManager.GetInstance(character.Map, session) ?? _mapManager.AddSessionToMap(character.Map, session, true);
                
                if (mapInstance == null)
                {
                    _logger.LogWarning("Failed to find or create map instance for character {CharacterId} for account {AccountId}", packet.CharacterId, session.AccountId);
                    return;
                }
            }
            else
            {
                // Map instance found, add the session to it
                if (!mapInstance.AddSession(session, true))
                {
                    _logger.LogWarning("Failed to add session {AccountId} to map instance {InstanceId}", session.AccountId, mapInstance.InstanceId);
                    return;
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
            character.Latency = session.RoundTripTime;

            await session.SendAsync(SCharacterSelectedPacket.Create(new CharacterInfo
            {
                CharacterId = character.Id!.Value,
                Name = character.Name,
                Level = character.Level,
                Class = character.Class,
                X = character.PositionX,
                Y = character.PositionY,
                Radius = _gameConfiguration.PlayerRadius
            },
            new MapInfo
            {
                MapId = mapInstance.MapId,
                InstanceId = mapInstance.InstanceId,
                Name = mapInstance.Name,
                Description = mapInstance.Description,
                Atlas = mapInstance.Atlas,
                Directory = mapInstance.Directory,
                Data = mapInstance.VirtualizedMap.TmxData,
                TilesetsData = mapInstance.VirtualizedMap.TsxData
            }, session.Encrypt));

            // save the character to the database
            await _databaseManager.Characters.Character.UpdateAsync(character);

            session.Character = character;
            
            _logger.LogInformation("Character {CharacterId} logged in for account {AccountId} at {Position}", character.Name, session.AccountId, character.Movement);

            // Send to everyone else, that this player is connected
            var availableSessions = _sessionManager.GetSessions().Values.Where(
                s => 
                    s.AccountId != session.AccountId 
                    && s is { Status: ConnectionStatus.Connected, Character: not null } 
                    && s.Character.InstanceId == session.Character.InstanceId
            );

            var tasks = availableSessions.Select(s => s.SendAsync(SPlayerConnectedPacket.Create(session.AccountId, session.Character.Id!.Value, session.Character.Name, s.Encrypt)));
            
            await Task.WhenAll(tasks);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    public async Task HandleCharacterDeletePacket(IRemoteSource source, CCharacterDeletePacket packet)
    {
        var session = _sessionManager.GetSession(source);
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {Address}", source.RemoteAddress);
            return;
        }
        
        if (session.InGame)
        {
            _logger.LogWarning("Session {AccountId} is already in game", session.AccountId);
            await session.SendAsync(SCharacterDeletedPacket.Create(session.AccountId, SCharacterDeletedResult.InGame, session.Encrypt));
            return;
        }
        
        var character = await _databaseManager.Characters.Character.FindByIdAndAccountAsync(packet.CharacterId, session.AccountId);
        if (character == null)
        {
            _logger.LogWarning("Character {CharacterId} not found for account {AccountId}", packet.CharacterId, session.AccountId);
            await session.SendAsync(SCharacterDeletedPacket.Create(session.AccountId, SCharacterDeletedResult.InternalError, session.Encrypt));
            return;
        }
        
        if (!await _databaseManager.Characters.Character.DeleteAsync(character))
        {
            _logger.LogWarning("Failed to delete character {CharacterId} for account {AccountId}", packet.CharacterId, session.AccountId);
            await session.SendAsync(SCharacterDeletedPacket.Create(session.AccountId, SCharacterDeletedResult.InternalError, session.Encrypt));
            return;
        }
        
        _logger.LogInformation("Character {CharacterId} deleted for account {AccountId}", packet.CharacterId, session.AccountId);
        await session.SendAsync(SCharacterDeletedPacket.Create(session.AccountId, SCharacterDeletedResult.Success, session.Encrypt));
    }

    public async Task HandleCharacterLoadedPacket(IRemoteSource source, CCharacterLoadedPacket packet)
    {
        var session = _sessionManager.GetSession(packet.AccountId);
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        var sessionLock = _sessionManager.GetSessionLock(session);

        await sessionLock.WaitAsync();

        try
        {

            if (!session.InGame)
            {
                _logger.LogWarning("Session {AccountId} is not in game", packet.AccountId);
                return;
            }
        
            var sessions = _sessionManager.GetSessions();
        
            // Send to this player, that everyone else is connected
            foreach (var otherSession in sessions.Values)
            {
                if (otherSession.AccountId == session.AccountId || !otherSession.InGame || otherSession.Character!.InstanceId != session.Character!.InstanceId)
                {
                    continue;
                }
            
                await session.SendAsync(SPlayerConnectedPacket.Create(otherSession.AccountId, otherSession.Character!.Id!.Value, otherSession.Character.Name, session.Encrypt));
            }
            
        }
        finally
        {
            sessionLock.Release();
        }
    }
}
