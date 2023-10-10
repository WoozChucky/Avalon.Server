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
}

public partial class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IAvalonConnectionManager _connectionManager;
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
        IAvalonConnectionManager connectionManager,
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
        _connectionManager = connectionManager;
        _databaseManager = databaseManager;
        _mapManager = mapManager;
        _creatureSpawner = creatureSpawner;
        _aiController = aiController;
        _poolManager = poolManager;
        _questManager = questManager;
        _cryptography = cryptography;
        _loopCounter = 0;
        
        _connectionManager.SessionLost += OnSessionLost;
        _connectionManager.SessionReconnected += OnPlayerReconnected;
        _connectionManager.SessionTimedOut += OnPlayerTimedOut;
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
        
        Task.Run(BroadcastLoop);
    }

    private async Task BroadcastLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await BroadcastGameState();
                
                if (DateTime.UtcNow - _lastMetricsUpdate > TimeSpan.FromMilliseconds(MetricsUpdateInterval))
                {
                    _lastMetricsUpdate = DateTime.UtcNow;
                    
                }
            
                await Task.Delay(26);
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Broadcast loop exception");
        }
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
        
        // Update all maps, including spawns, AI, etc.
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
        
        var packet = SPlayerDisconnectedPacket.Create(session.AccountId, session.Character!.Id);
            
        await BroadcastToOthers(session.AccountId, packet, true);
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

    #region Broadcasts

    private async Task BroadcastGameState()
    {
        // Broadcast player positions
        foreach (var session in _connectionManager.GetInGameSessions())
        {
            if (session?.Character == null) continue;
            
            var playerUpdatePacket = SPlayerPositionUpdatePacket.Create(
                session.AccountId,
                session.Character.Id,
                session.Character.Movement.Position.X, 
                session.Character.Movement.Position.Y,
                session.Character.Movement.Velocity.X,
                session.Character.Movement.Velocity.Y,
                session.Character.IsChatting,
                session.Character.ElapsedGameTime
            );
            
            await BroadcastToOthersInInstance(session.AccountId, playerUpdatePacket, session.Character.InstanceId);
            
            var mapInstance = _mapManager.GetInstance(session.Character.Map, Guid.Parse(session.Character.InstanceId));
            if (mapInstance == null) continue;
            
            foreach (var creature in mapInstance.Creatures.Values)
            {
                var creaturePacket = SNpcUpdatePacket.Create(
                    creature.Id,
                    creature.Name,
                    creature.Position.X, 
                    creature.Position.Y,
                    creature.Velocity.X,
                    creature.Velocity.Y
                );
                
                await BroadcastToInstance(creaturePacket, session.Character.InstanceId);
            }
        }
    }
    
    private async Task BroadcastToOthers(int except, NetworkPacket packet, bool onlineOnly = false)
    {
        if (onlineOnly)
        {
            var availablePlayers = _connectionManager.GetSessions().Values.Where(
                p => p.AccountId != except
                     && p is { Status: ConnectionStatus.Connected, InGame: true }).Select(p => p.SendAsync(packet));
        
            await Task.WhenAll(availablePlayers);
        }
        else
        {
            var availablePlayers = _connectionManager.GetSessions().Values.Where(
                p => p.AccountId != except
                     && p is { Status: ConnectionStatus.Connected, InGame: false }).Select(p => p.SendAsync(packet));
        
            await Task.WhenAll(availablePlayers);
        }
    }
    
    private async Task BroadcastAll(NetworkPacket packet)
    {
        var availablePlayers = _connectionManager.GetSessions().Values.Where(
            p => p.Status == ConnectionStatus.Connected).Select(p => p.SendAsync(packet));

        await Task.WhenAll(availablePlayers);
    }
    
    private async Task BroadcastToInstance(NetworkPacket packet, string instanceId)
    {
        var availablePlayers = _connectionManager.GetSessions().Values.Where(
            p => p.Status == ConnectionStatus.Connected
            && p.Character != null && p.Character.InstanceId == instanceId).Select(p => p.SendAsync(packet));

        await Task.WhenAll(availablePlayers);
    }
    
    private async Task BroadcastToOthersInInstance(int except, NetworkPacket packet, string instanceId)
    {
        
        if (packet.Header.Type == NetworkPacketType.SMSG_CHARACTER_CONNECTED ||
            packet.Header.Type == NetworkPacketType.SMSG_CHARACTER_DISCONNECTED)
        {
            var availablePlayers = _connectionManager.GetSessions().Values.Where(
                p => p.AccountId != except && p.Status == ConnectionStatus.Connected
                                           && p.Character != null && p.Character.InstanceId == instanceId);

            foreach (var availablePlayer in availablePlayers)
            {
                _logger.LogDebug("Sending packet {PacketType} to {CharacterName}", packet.Header.Type, availablePlayer.Character?.Name);
            }
            
            var tasks = availablePlayers.Select(p => p.SendAsync(packet));
            
            await Task.WhenAll(tasks);
        }
        else
        {
            var availablePlayers = _connectionManager.GetSessions().Values.Where(
                p => p.AccountId != except && p.Status == ConnectionStatus.Connected
                                           && p.Character != null && p.Character.InstanceId == instanceId).Select(p => p.SendAsync(packet));
            
            await Task.WhenAll(availablePlayers);
        }
    }

    #endregion

    #region Packet Handlers

    public Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session is not { InGame: true }) return Task.CompletedTask;
        
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
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session?.Character == null) return;

        if (packet.MapId == session.Character.Map)
        {
            _logger.LogWarning("Character {CharacterId} tried to teleport to the same map", packet.CharacterId);
            return;
        }

        var currentMapInstance = _mapManager.GetInstance(session.Character.Map, session.Character.Id);
        if (currentMapInstance == null)
        {
            _logger.LogWarning("Character {CharacterId} tried to teleport, and is coming from a map that doesn't exist", packet.CharacterId);
            return;
        }

        if (!_mapManager.RemoveCharacterFromMap(session.Character.Map, session.Character.Id))
        {
            _logger.LogWarning("Character {CharacterId} tried to teleport, and is coming from a map that doesn't exist", packet.CharacterId);
            return;
        }

        var newInstance = _mapManager.GenerateInstance(packet.MapId);
        newInstance.AddCharacter(session.Character.Id);
        
        float x;
        float y;

        if (packet.MapId == 1) // TODO: Hardcoded starting coordinates
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
        }, x, y));
        
        // Warn about other players, that the player left the old instance
        await BroadcastToOthersInInstance(session.AccountId, SPlayerDisconnectedPacket.Create(session.AccountId, packet.CharacterId), session.Character.InstanceId);
        
        // Warn player in the new instance, that a new player has joined
        await BroadcastToOthersInInstance(session.AccountId, SPlayerConnectedPacket.Create(session.AccountId, packet.CharacterId, session.Character.Name), newInstance.InstanceId.ToString());
        
        session.Character.Map = packet.MapId;
        session.Character.InstanceId = newInstance.InstanceId.ToString();
        session.Character.Movement.Position = new Vector2(x, y);
        session.Character.PositionX = x;
        session.Character.PositionY = y;

        await _databaseManager.Characters.Character.UpdateAsync(session.Character);
    }

    public async Task HandleServerInfoPacket(IRemoteSource source, CRequestServerInfoPacket packet)
    {
        var client = source.AsTcpClient();
        
        _logger.LogDebug("Handling server info packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
        if (packet.ClientVersion != 1_000_000)
        {
            _logger.LogWarning("Client {EndPoint} is using an invalid version", client.Socket.RemoteEndPoint);
            client.Dispose();
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
        
        _connectionManager.AddSession(source, _cryptography.GetKeyPair(), packet.PublicKey);

        var session = _connectionManager.GetSession(source);

        var data = session?.GenerateHandshakeData() ?? throw new InvalidOperationException("Session not found");
        
        var result = SHandshakePacket.Create(data, session.Encrypt);
        
        await source.SendAsync(result);
    }

    public async Task HandleHandshakePacket(IRemoteSource source, CHandshakePacket packet)
    {
        var session = _connectionManager.GetSession(source);
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
