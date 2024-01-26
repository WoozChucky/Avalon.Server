using System.Numerics;
using System.Reflection;
using Avalon.Database;
using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Avalon.Game.Pools;
using Avalon.Game.Quests;
using Avalon.Game.Scripts;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Handshake;
using Avalon.Network.Packets.Map;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.World;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public interface IAvalonGame
{
    void Start();
    void Stop();
    void Update(TimeSpan deltaTime);
    bool IsRunning();
    void IncrementLoopCounter();
    long GetLoopCounter();
    
    Task HandlePingPacket(IRemoteSource source, CPingPacket packet);
    Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet);
    Task HandleChatMessagePacket(IRemoteSource source, CChatMessagePacket packet);
    Task HandleOpenChatPacket(IRemoteSource source, COpenChatPacket packet);
    Task HandleCloseChatPacket(IRemoteSource source, CCloseChatPacket packet);
    Task HandleAuthPacket(IRemoteSource source, CAuthPacket packet);
    Task HandleGroupInviteResultPacket(IRemoteSource source, CGroupInviteResultPacket packet);
    Task HandleCharacterSelectedPacket(IRemoteSource source, CCharacterSelectedPacket packet);
    Task HandleCharacterListPacket(IRemoteSource source, CCharacterListPacket packet);
    Task HandleCharacterCreatePacket(IRemoteSource source, CCharacterCreatePacket packet);
    Task HandleCharacterDeletePacket(IRemoteSource source, CCharacterDeletePacket packet);
    Task HandleCharacterLoadedPacket(IRemoteSource source, CCharacterLoadedPacket packet);
    Task HandleLogoutPacket(IRemoteSource source, CLogoutPacket packet);
    Task HandleMapTeleportPacket(IRemoteSource source, CMapTeleportPacket packet);
    Task HandleInteractPacket(IRemoteSource source, CInteractPacket packet);
    Task HandleQuestListPacket(IRemoteSource source, CQuestStatusPacket packet);
    Task HandleQuestStatusPacket(IRemoteSource source, CQuestStatusPacket packet);
    Task HandleServerInfoPacket(IRemoteSource source, CRequestServerInfoPacket packet);
    Task HandleClientInfoPacket(IRemoteSource source, CClientInfoPacket packet);
    Task HandleHandshakePacket(IRemoteSource source, CHandshakePacket packet);
    Task HandleRegisterPacket(IRemoteSource source, CRegisterPacket packet);
}

public partial class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IAvalonSessionManager _sessionManager;
    private readonly IDatabaseManager _databaseManager;
    private readonly IAvalonMapManager _mapManager;
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly IAIController _aiController;
    private readonly IPoolManager _poolManager;
    private readonly IQuestManager _questManager;
    private readonly ICryptoManager _cryptography;
    private volatile bool _isRunning;
    private long _loopCounter;
    private DateTime _lastMetricsUpdate = DateTime.UtcNow;
    private const int MetricsUpdateInterval = 1000;

    public AvalonGame(ILoggerFactory loggerFactory,
        IAvalonSessionManager sessionManager,
        IDatabaseManager databaseManager,
        IAvalonMapManager mapManager,
        ICreatureSpawner creatureSpawner,
        IAIController aiController,
        IPoolManager poolManager,
        IQuestManager questManager,
        ICryptoManager cryptography)
    {
        _logger = loggerFactory.CreateLogger<AvalonGame>() ?? throw new ArgumentNullException(nameof(loggerFactory));
        _cts = new CancellationTokenSource();
        _sessionManager = sessionManager;
        _databaseManager = databaseManager;
        _mapManager = mapManager;
        _creatureSpawner = creatureSpawner;
        _aiController = aiController;
        _poolManager = poolManager;
        _questManager = questManager;
        _cryptography = cryptography;
        _loopCounter = 0;
        
        _sessionManager.SessionLost += OnSessionLost;
        _sessionManager.SessionReconnected += OnPlayerReconnected;
        _sessionManager.SessionTimedOut += OnPlayerTimedOut;
    }

    public void Start()
    {
        _logger.LogInformation("Loading game data");
        
        _aiController.LoadScripts();
        _creatureSpawner.LoadCreatures();
        _mapManager.LoadMaps();
        _questManager.LoadQuests();
        
        _logger.LogInformation("Finished loading game data");
        
        _isRunning = true;
        
        _logger.LogInformation("Starting game loop");
    }

    public void Stop()
    {
        _logger.LogInformation("Stopping game loop");
        _cts.Cancel();
        _isRunning = false;
    }
    
    public bool IsRunning()
    {
        return _isRunning;
    }

    public void IncrementLoopCounter()
    {
        Interlocked.Increment(ref _loopCounter);
    }

    public long GetLoopCounter()
    {
        Interlocked.Read(ref _loopCounter);
        return _loopCounter;
    }

    public void Update(TimeSpan deltaTime)
    {
        
        // Process AvalonSession packets
        
        // Process Map updates
        foreach (var (_, mapInstances) in _mapManager.GetInstances())
        {
            foreach (var (_, mapInstance) in mapInstances)
            {
                _poolManager.Update(deltaTime, mapInstance);
                _aiController.Update(mapInstance, deltaTime);
                mapInstance.Update(deltaTime);
            }
        }
    }

    #region Player Events

    private async void OnSessionLost(object? sender, AvalonSession session)
    {
        if (!session.InGame) return;
        
        _logger.LogDebug("Session lost for account {AccountId}", session.AccountId);
        
        var availableSessions = _sessionManager.GetSessions().Values.Where(
            s => 
                s.AccountId != session.AccountId 
                && s is { Status: ConnectionStatus.Connected, Character: not null }
        );

        var tasks = availableSessions.Select(s => s.SendAsync(SPlayerDisconnectedPacket.Create(session.AccountId, session.Character!.Id!.Value, s.Encrypt)));
            
        await Task.WhenAll(tasks);
    }
    
    private void OnPlayerTimedOut(object? sender, AvalonSession session)
    {
        _logger.LogWarning("TODO: Handle session timeout");
    }
    
    private async void OnPlayerReconnected(object? sender, AvalonSession session)
    {
        _logger.LogWarning("TODO: Handle session reconnected");
    }

    #endregion

    #region Packet Handlers

    public Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet)
    {
        var session = _sessionManager.GetSession(source);
        if (session is not { InGame: true }) return Task.CompletedTask;
        if (session.AccountId != packet.AccountId) return Task.CompletedTask;
        
        Vector2 velocity;
        
        if (float.IsNaN(packet.VelocityX) && float.IsNaN(packet.VelocityY))
        {
            velocity = Vector2.Zero;
        }
        else
        {
            velocity = new Vector2(packet.VelocityX, packet.VelocityY);
        }
        
        session.Character!.Movement.Position = new Vector2(packet.X, packet.Y);
        session.Character!.Movement.Velocity = velocity;
        session.Character!.ElapsedGameTime = packet.ElapsedGameTime;
        
        return Task.CompletedTask;
    }

    public async Task HandleMapTeleportPacket(IRemoteSource source, CMapTeleportPacket packet)
    {
        var session = _sessionManager.GetSession(packet.AccountId);
        
        if (session == null)
        {
            _logger.LogWarning("Session not found for account {AccountId}", packet.AccountId);
            return;
        }
        
        if (!session.InMap)
        {
            _logger.LogWarning("Session {AccountId} is not in a map", packet.AccountId);
            return;
        }

        if (packet.MapId == session.Character!.Map)
        {
            _logger.LogWarning("Character {CharacterId} tried to teleport to the same map", packet.CharacterId);
            return;
        }

        if (!_mapManager.RemoveSessionFromMap(session))
        {
            _logger.LogWarning(
                "Character {CharacterId} tried to teleport, but couldn't be removed from source instance {InstanceId}",
                packet.CharacterId, session.Character.InstanceId);
            return;
        }

        var newInstance = _mapManager.GenerateInstance(packet.MapId);

        if (!newInstance.AddSession(session))
        {
            _logger.LogWarning("Character {CharacterId} tried to teleport, and failed", packet.CharacterId);
            return;
        }
        
        float x;
        float y;

        if (packet.MapId == 1) // TODO: Hardcoded starting coordinates, and map ids :(
        {
            x = 31;
            y = 801;
        }
        else
        {
            x = 903;
            y = 552;
        }

        // Teleport the player
        await session.SendAsync(SMapTeleportPacket.Create(session.AccountId, packet.CharacterId, new MapInfo
        {
            InstanceId = newInstance.InstanceId,
            Atlas = newInstance.Atlas,
            Directory = newInstance.Directory,
            MapId = packet.MapId,
            Name = newInstance.Name,
            Description = newInstance.Description,
            Data = newInstance.VirtualizedMap.TmxData,
            TilesetsData = newInstance.VirtualizedMap.TsxData,
        }, x, y, session.Encrypt));
        
        
        
        // Warn other players, that the current player left the old instance
        var sessionsInCurrentInstance = _sessionManager.GetSessions().Values.Where(
            s => 
                s.AccountId != session.AccountId 
                && s is { Status: ConnectionStatus.Connected, Character: not null } 
                && s.Character.InstanceId == session.Character.InstanceId
        );
        
        var tasks = sessionsInCurrentInstance.Select(s => s.SendAsync(SPlayerDisconnectedPacket.Create(session.AccountId, packet.CharacterId, s.Encrypt)));
        
        await Task.WhenAll(tasks);
        
        
        // Warn other players, that the current player joined the new instance
        var sessionsInNewInstance = _sessionManager.GetSessions().Values.Where(
            s => 
                s.AccountId != session.AccountId 
                && s is { Status: ConnectionStatus.Connected, Character: not null } 
                && s.Character.InstanceId == newInstance.InstanceId.ToString()
        );
        
        tasks = sessionsInNewInstance.Select(s => s.SendAsync(SPlayerConnectedPacket.Create(session.AccountId, packet.CharacterId, session.Character.Name, s.Encrypt)));
        
        await Task.WhenAll(tasks);
        
        session.Character.Map = packet.MapId;
        session.Character.InstanceId = newInstance.InstanceId.ToString();
        session.Character.Movement.Position = new Vector2(x, y);
        session.Character.PositionX = x;
        session.Character.PositionY = y;

        await _databaseManager.Characters.Character.UpdateAsync(session.Character);
    }

    public async Task HandleServerInfoPacket(IRemoteSource source, CRequestServerInfoPacket packet)
    {
        _logger.LogDebug("Handling server info packet from {EndPoint}", source.RemoteAddress);
        
        if (packet.ClientVersion != 1_000_000)
        {
            _logger.LogWarning("Client {EndPoint} is using an invalid version", source.RemoteAddress);
            source.Dispose();
            throw new NotImplementedException("Invalid client version not implemented yet");
        }
        
        var result = SServerInfoPacket.Create(
            1_000_000, // TODO: Hardcoded server version
            _cryptography.GetPublicKey()
        );

        await source.SendAsync(result);
    }

    public async Task HandleClientInfoPacket(IRemoteSource source, CClientInfoPacket packet)
    {
        if (packet.PublicKey == null || packet.PublicKey.Length == 0)
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key", source.RemoteAddress);
            return;
        }

        if (packet.PublicKey.Length != _cryptography.GetValidKeySize())
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid public key size", source.RemoteAddress);
            return;
        }
        
        _sessionManager.AddSession(source, _cryptography.GetKeyPair(), packet.PublicKey);

        var session = _sessionManager.GetSession(source);
        
        if (session == null)
        {
            _logger.LogWarning("Session not found for client {EndPoint}", source.RemoteAddress);
            return;
        }

        var data = session?.GenerateHandshakeData() ?? throw new InvalidOperationException("Session not found");
        
        var result = SHandshakePacket.Create(data, session.Encrypt);
        
        await source.SendAsync(result);
    }

    public async Task HandleHandshakePacket(IRemoteSource source, CHandshakePacket packet)
    {
        var session = _sessionManager.GetSession(source);
        
        if (session == null)
        {
            _logger.LogWarning("Client {EndPoint} sent a handshake packet, but no session was found", source.RemoteAddress);
            return;
        }
        
        if (!session.VerifyHandshakeData(packet.HandshakeData))
        {
            _logger.LogWarning("Client {EndPoint} sent an invalid handshake data", source.RemoteAddress);
            
            // TODO: Send a packet to the client, and disconnect
            
            return;
        }

        var result = SHandshakeResultPacket.Create(true, session.Encrypt);
        
        await source.SendAsync(result);
    }

    public async Task HandlePingPacket(IRemoteSource source, CPingPacket packet)
    {
        var response = SPongPacket.Create(packet.SequenceNumber, packet.Ticks);

        await source.SendAsync(response);
    }
    
    #endregion
}
