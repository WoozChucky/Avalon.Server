using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using Avalon.Database;
using Avalon.Game.Creatures;
using Avalon.Game.Entities;
using Avalon.Game.Maps;
using Avalon.Game.Maps.Virtual;
using Avalon.Game.Pools;
using Avalon.Game.Scripts;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Map;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public interface IAvalonGame
{
    void Start();
    void Stop();
    Task HandleServerVersionPacket(IRemoteSource source, CRequestServerVersionPacket packet);
    Task HandlePingPacket(IRemoteSource source, CPingPacket packet);
    Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet);
    Task HandleChatMessagePacket(IRemoteSource source, CChatMessagePacket packet);
    Task HandleOpenChatPacket(IRemoteSource source, COpenChatPacket packet);
    Task HandleCloseChatPacket(IRemoteSource source, CCloseChatPacket packet);
    Task HandleAuthPacket(IRemoteSource source, CAuthPacket packet);
    Task HandleGroupInviteResultPacket(IRemoteSource source, CGroupInviteResultPacket packet);
    Task HandleAuthPatchPacket(IRemoteSource source, CAuthPatchPacket packet);
    Task HandleCharacterSelectedPacket(IRemoteSource source, CCharacterSelectedPacket packet);
    Task HandleCharacterListPacket(IRemoteSource source, CCharacterListPacket packet);
    Task HandleCharacterCreatePacket(IRemoteSource source, CCharacterCreatePacket packet);
    Task HandleCharacterDeletePacket(IRemoteSource source, CCharacterDeletePacket packet);
    Task HandleCharacterLoadedPacket(IRemoteSource source, CCharacterLoadedPacket packet);
    Task HandleLogoutPacket(IRemoteSource source, CLogoutPacket packet);

    Task HandleMapTeleportPacket(IRemoteSource source, CMapTeleportPacket packet);
}

public partial class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IAvalonConnectionManager _connectionManager;
    private readonly IDatabaseManager _databaseManager;
    private readonly IAvalonMapManager _mapManager;
    private readonly ICreatureSpawner _creatureSpawner;
    private readonly IAIController _aiController;
    private readonly IPoolManager _poolManager;

    private readonly ConcurrentDictionary<int, Uriel> _npcs;
    private readonly VirtualMap _map;

    public AvalonGame(ILogger<AvalonGame> logger, 
        IPacketSerializer packetSerializer, 
        IAvalonConnectionManager connectionManager,
        IDatabaseManager databaseManager,
        IAvalonMapManager mapManager,
        ICreatureSpawner creatureSpawner,
        IAIController aiController,
        IPoolManager poolManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();
        _packetSerializer = packetSerializer;
        _connectionManager = connectionManager;
        _databaseManager = databaseManager;
        _mapManager = mapManager;
        _creatureSpawner = creatureSpawner;
        _aiController = aiController;
        _poolManager = poolManager;
        _npcs = new ConcurrentDictionary<int, Uriel>();
        
        _connectionManager.SessionLost += OnSessionLost;
        _connectionManager.SessionReconnected += OnPlayerReconnected;
        _connectionManager.SessionTimedOut += OnPlayerTimedOut;
        
        _map = new VirtualMap(1, "Tutorial.tmx", "Maps/");

        _npcs.TryAdd(1, new Uriel());
    }

    public async void Start()
    {
        _aiController.LoadScripts();
        await _creatureSpawner.LoadCreaturesAsync();
        await _mapManager.LoadMaps();

        _logger.LogInformation("Starting game loop");

#pragma warning disable CS4014
        Task.Run(BroadcastLoop);
#pragma warning restore CS4014
        
        const int updatesPerSecond = 60;
        const int maxFrameTimeMs = 100;

        var desiredFrameTime = TimeSpan.FromMilliseconds(1000.0 / updatesPerSecond);
        var accumulatedTime = TimeSpan.Zero;
        var previousTime = DateTime.UtcNow;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var currentTime = DateTime.UtcNow;
                var elapsedTime = currentTime - previousTime;
                previousTime = currentTime;
                
                if (elapsedTime > TimeSpan.FromMilliseconds(maxFrameTimeMs))
                {
                    _logger.LogWarning("Exceeded max frame time");
                    elapsedTime = TimeSpan.FromMilliseconds(maxFrameTimeMs);
                }
                
                accumulatedTime += elapsedTime;
        
                while (accumulatedTime >= desiredFrameTime)
                {
                    accumulatedTime -= desiredFrameTime;
                    Update(desiredFrameTime);
                }
        
                var sleepTime = desiredFrameTime - accumulatedTime;
                if (sleepTime > TimeSpan.Zero)
                {
                    await Task.Delay(sleepTime);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Game loop cancelled");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Game loop exception");
        }
    }

    private async Task BroadcastLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                await BroadcastGameState();
            
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
    }

    private void Update(TimeSpan deltaTime)
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
        
        
        foreach (var session in _connectionManager.GetSessions().Values.Where(s => s.InGame))
        {
            /*
            if (_map.IsObjectColliding(player.Bounds))
            {
                player.InvertDirection();
            }
            */
        }
        
        foreach (var (id, entity) in _npcs)
        {
            entity.Update(deltaTime);

            /*
            foreach (var (_, player) in _players)
            {
                if (entity.Bounds.IntersectsWith(player.Bounds))
                {
                    if (entity.State != UrielState.Collided)
                    {
                        entity.State = UrielState.Collided;
                        entity.PreviousVelocity = entity.Velocity;
                        entity.Velocity = Vector2.Zero;
                        break;
                    }
                }
                else
                {
                    if (entity.State == UrielState.Collided)
                    {
                        entity.State = UrielState.Walking;
                        entity.Velocity = new Vector2(0, 0);
                        //entity.InvertDirection();
                    }
                }
            }
            */
            
            if (_map.IsObjectColliding(entity.Bounds))
            {
                entity.InvertDirection();
                //entity.Position = entity.PreviousPosition;
                //entity.Velocity = entity.PreviousVelocity;
                //entity.RandomizeDirection();
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
            if (session.Character == null) continue;
            
            var packet = SPlayerPositionUpdatePacket.Create(
                session.AccountId,
                session.Character.Id,
                session.Character.Movement.Position.X, 
                session.Character.Movement.Position.Y,
                session.Character.Movement.Velocity.X,
                session.Character.Movement.Velocity.Y,
                session.Character.IsChatting,
                session.Character.ElapsedGameTime
            );
            
            await BroadcastToInstance(packet, session.Character.InstanceId);
        }
        
        // Broadcast NPC positions
        foreach (var map in _mapManager.GetInstances().Values.Select(v => v.Values).SelectMany(v => v))
        {
            foreach (var (id, creature) in map.Creatures)
            {
                // _logger.LogDebug("Broadcasting creature {id} position {Position}", id, creature.Position);
                var packet = SNpcUpdatePacket.Create(
                    creature.Id,
                    creature.Name,
                    creature.Position.X, 
                    creature.Position.Y,
                    creature.Velocity.X,
                    creature.Velocity.Y
                );
                await BroadcastAll(packet);
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
        var availablePlayers = _connectionManager.GetSessions().Values.Where(
            p =>  p.AccountId != except && p.Status == ConnectionStatus.Connected
                  && p.Character != null && p.Character.InstanceId == instanceId).Select(p => p.SendAsync(packet));

        await Task.WhenAll(availablePlayers);
    }

    #endregion

    #region Packet Handlers

    public Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet)
    {
        var session = _connectionManager.GetSession(packet.AccountId);
        if (session == null || !session.InGame) return Task.CompletedTask;
        
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
        if (session == null || session.Character == null) return;

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
            Name = newInstance.Name
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

    public async Task HandleServerVersionPacket(IRemoteSource source, CRequestServerVersionPacket packet)
    {
        var client = (TcpClient) source;
        
        _logger.LogDebug("Handling server version packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
        var result = SServerVersionPacket.Create(
            Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Minor ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Build ?? 0,
            Assembly.GetExecutingAssembly().GetName().Version?.Revision ?? 0
        );
        
        await _packetSerializer.SerializeToNetwork(client.Stream, result);
    }

    public async Task HandlePingPacket(IRemoteSource source, CPingPacket packet)
    {
        var client = source.AsUdpClient();

        var response = SPongPacket.Create(packet.SequenceNumber, packet.Ticks);

        await source.SendAsync(response);
    }
    
    #endregion
}
