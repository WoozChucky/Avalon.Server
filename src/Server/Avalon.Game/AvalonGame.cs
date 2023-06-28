using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using Avalon.Game.Entities;
using Avalon.Game.Handlers;
using Avalon.Map;
using Avalon.Network.Abstractions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Microsoft.Extensions.Logging;

namespace Avalon.Game;

public interface IAvalonGame
{
    void Start();
    void Stop();
    Task HandleServerVersionPacket(IRemoteSource source, CRequestServerVersionPacket packet);
    Task HandlePingPacket(IRemoteSource source, CPingPacket packet);
    Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet);
}

public class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IAvalonConnectionManager _connectionManager;

    private readonly ConcurrentDictionary<Guid, Player> _players;
    private readonly ConcurrentDictionary<Guid, Uriel> _npcs;
    private readonly ServerMap _map;

    public AvalonGame(ILogger<AvalonGame> logger, 
        IPacketSerializer packetSerializer, 
        IAvalonConnectionManager connectionManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();
        _packetSerializer = packetSerializer;
        _connectionManager = connectionManager;
        _players = new ConcurrentDictionary<Guid, Player>();
        _npcs = new ConcurrentDictionary<Guid, Uriel>();
        _connectionManager.PlayerConnected += OnPlayerConnected;
        _connectionManager.PlayerDisconnected += OnPlayerDisconnected;
        _connectionManager.PlayerReconnected += OnPlayerReconnected;
        _connectionManager.PlayerTimedOut += OnPlayerTimedOut;
        
        _map = new ServerMap("Tutorial", "Serene_Village_32x32");

        _npcs.TryAdd(Guid.NewGuid(), new Uriel());
    }

    public async void Start()
    {
        _logger.LogInformation("Starting game loop");
        
        var previousTime = DateTime.UtcNow;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var currentTime = DateTime.UtcNow;
                var deltaTime = currentTime - previousTime;
                previousTime = currentTime;
        
                Update(deltaTime);
                
                await BroadcastGameState();
            
                await Task.Delay(26);
            }
        }
        catch (OperationCanceledException e)
        {
            _logger.LogInformation("Game loop cancelled");
        }
    }

    public void Stop()
    {
        _logger.LogInformation("Stopping game loop");
        _cts.Cancel();
    }

    private void Update(TimeSpan deltaTime)
    {
        foreach (var (id, player) in _players)
        {
            if (_map.IsObjectColliding(player.Bounds))
            {
                player.InvertDirection();
            }
        }
        
        foreach (var (id, entity) in _npcs)
        {
            entity.Update(deltaTime);

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
                        entity.Velocity = entity.PreviousVelocity;
                        entity.InvertDirection();
                    }
                }
            }
            
            if (_map.IsObjectColliding(entity.Bounds))
            {
                entity.InvertDirection();
            }
        }
    }

    #region Player Events

    private async void OnPlayerConnected(object? sender, AvalonConnection connection)
    {
        if (_players.TryAdd(connection.Id, new Player(connection, new Character
            {
                Position = Vector2.Zero,
                Velocity = Vector2.Zero,
                Bounds = new Rectangle(0, 0, 32, 32),
                ElapsedGameTime = 0
            })))
        {
            _logger.LogInformation("Player {PlayerId} connected", connection.Id);
            
            foreach (var (existingId, existingPlayer) in _players)
            {
                if (existingId == connection.Id)
                {
                    continue;
                }
                // Send to this player, that everyone else is connected
                await connection.SendAsync(SPlayerConnectedPacket.Create(existingId));
            }
            // Send to everyone else, that this player is connected
            await BroadcastToOthers(connection.Id, SPlayerConnectedPacket.Create(connection.Id));
        }
        else
        {
            if (_players.ContainsKey(connection.Id))
            {
                OnPlayerReconnected(null, connection);
            }
            _logger.LogError("Failed to add player {PlayerId}", connection.Id);
        }
    }
    
    private async void OnPlayerDisconnected(object? sender, AvalonConnection connection)
    {
        if (_players.TryRemove(connection.Id, out var player))
        {
            _logger.LogInformation("Player {PlayerId} disconnected", connection.Id);
            
            var packet = SPlayerDisconnectedPacket.Create(connection.Id);
            
            await BroadcastToOthers(connection.Id, packet);
        }
        else
        {
            _logger.LogError("Failed to remove player {PlayerId}", connection.Id);
        }
    }
    
    private void OnPlayerTimedOut(object? sender, AvalonConnection connection)
    {
        // TODO: Handle player timeout
    }
    
    private async void OnPlayerReconnected(object? sender, AvalonConnection connection)
    {
        foreach (var (existingId, existingPlayer) in _players)
        {
            if (existingId == connection.Id)
            {
                continue;
            }
            // Send to this player, that everyone else is connected
            await connection.SendAsync(SPlayerConnectedPacket.Create(existingId));
        }
        // Send to everyone else, that this player is connected
        //await BroadcastToOthers(connection.Id, SPlayerConnectedPacket.Create(connection.Id));
    }

    #endregion

    #region Broadcasts

    private async Task BroadcastGameState()
    {
        // Broadcast player positions
        foreach (var (id, player) in _players)
        {
            var packet = SPlayerPositionUpdatePacket.Create(
                id, 
                player.Position.X, 
                player.Position.Y,
                player.Velocity.X,
                player.Velocity.Y,
                player.Character.ElapsedGameTime
            );
            await BroadcastAll(packet);
        }
        
        // Broadcast NPC positions
        foreach (var (id, npc) in _npcs)
        {
            var packet = SNpcUpdatePacket.Create(
                id, 
                npc.Position.X, 
                npc.Position.Y,
                npc.Velocity.X,
                npc.Velocity.Y
            );
            await BroadcastAll(packet);
        }
    }
    
    private async Task BroadcastToOthers(Guid except, NetworkPacket packet)
    {
        var availablePlayers = _players.Values.Where(
            p => p.Id != except
                 && p.Connection.Status == ConnectionStatus.Connected).ToList();
        
        foreach (var player in availablePlayers)
        {
            try
            {
                await player.Connection.SendAsync(packet);
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
    
    private async Task BroadcastAll(NetworkPacket packet)
    {
        foreach (var (_, player) in _players)
        {
            await player.Connection.SendAsync(packet);
        }
    }

    #endregion

    #region Packet Handlers

    public async Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet)
    {
        if (_players.TryGetValue(packet.ClientId, out var player))
        {
            player.Position = new Vector2(packet.X, packet.Y);
            player.Velocity = new Vector2(packet.VelocityX, packet.VelocityY);
            player.Bounds = new Rectangle(
                (int) player.Position.X,
                (int) player.Position.Y,
                32,
                32
            );
            player.Character.ElapsedGameTime = packet.ElapsedGameTime;
            // TODO: player.Character.LastUpdated = DateTime.UtcNow;
        }
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
