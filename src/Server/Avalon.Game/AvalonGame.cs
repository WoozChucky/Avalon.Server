using System.Collections.Concurrent;
using System.Reflection;
using Avalon.Game.Entities;
using Avalon.Game.Handlers;
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

public class Player : IEntity
{
    public Guid Id { get; set; }
    public AvalonConnection Connection { get; private set; }
    public Character Character { get; private set; }
    
    public Player(AvalonConnection connection, Character character)
    {
        Id = connection.Id;
        Connection = connection;
        Character = character;
    }
    
    public void Update(TimeSpan deltaTime)
    {
        
    }
}

public class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IAvalonConnectionManager _connectionManager;
    private readonly IAvalonMovementManager _movementManager;

    private readonly ConcurrentDictionary<Guid, Player> _players;
    private readonly ConcurrentDictionary<Guid, IEntity> _npcs;

    public AvalonGame(ILogger<AvalonGame> logger, 
        IPacketSerializer packetSerializer, 
        IAvalonConnectionManager connectionManager,
        IAvalonMovementManager movementManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();
        _packetSerializer = packetSerializer;
        _connectionManager = connectionManager;
        _movementManager = movementManager;
        _players = new ConcurrentDictionary<Guid, Player>();
        _npcs = new ConcurrentDictionary<Guid, IEntity>();
        _connectionManager.PlayerConnected += OnPlayerConnected;
        _connectionManager.PlayerDisconnected += OnPlayerDisconnected;
        _connectionManager.PlayerReconnected += OnPlayerReconnected;
        _connectionManager.PlayerTimedOut += OnPlayerTimedOut;
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
            
                await Task.Delay(50);
            }
        }
        catch (OperationCanceledException e)
        {
            _logger.LogInformation("Game loop cancelled");
        }
    }

    private async void OnPlayerConnected(object? sender, AvalonConnection connection)
    {
        if (_players.TryAdd(connection.Id, new Player(connection, new Character
            {
                X = 0,
                Y = 0,
                VelocityX = 0,
                VelocityY = 0,
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
    
    private void OnPlayerReconnected(object? sender, AvalonConnection connection)
    {
        // TODO: Handle player reconnection
    }
    
    private async Task BroadcastToOthers(Guid except, NetworkPacket packet)
    {
        var availablePlayers = _players.Values.Where(
            p => p.Id != except
            && p.Connection.Status != ConnectionStatus.Connected).ToList();
        
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

    public void Stop()
    {
        _logger.LogInformation("Stopping game loop");
        _cts.Cancel();
    }

    private void Update(TimeSpan deltaTime)
    {
        foreach (var entity in _npcs.Values)
        {
            entity.Update(deltaTime);
        }
    }
    
    private async Task BroadcastGameState()
    {
        // Broadcast player positions
        foreach (var (id, player) in _players)
        {
            var packet = SPlayerPositionUpdatePacket.Create(
                id, 
                player.Character.X, 
                player.Character.Y,
                player.Character.VelocityX,
                player.Character.VelocityY,
                player.Character.ElapsedGameTime
            );
            await BroadcastToOthers(id, packet);
        }
    }
    
    public async Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet)
    {
        if (_players.TryGetValue(packet.ClientId, out var player))
        {
            player.Character.X = packet.X;
            player.Character.Y = packet.Y;
            player.Character.VelocityX = packet.VelocityX;
            player.Character.VelocityY = packet.VelocityY;
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

        var response = SPongPacket.Create(packet.Ticks);
        
        await using var ms = new MemoryStream();
        await _packetSerializer.SerializeToNetwork(ms, response);
        
        await client.SendResponseAsync(ms.ToArray());
    }
}
