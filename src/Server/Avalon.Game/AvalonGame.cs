using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using Avalon.Game.Entities;
using Avalon.Map;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Generic;
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
}

public class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IAvalonConnectionManager _connectionManager;

    private readonly ConcurrentDictionary<string, Player> _players;
    private readonly ConcurrentDictionary<string, Uriel> _npcs;
    private readonly ServerMap _map;

    public AvalonGame(ILogger<AvalonGame> logger, 
        IPacketSerializer packetSerializer, 
        IAvalonConnectionManager connectionManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();
        _packetSerializer = packetSerializer;
        _connectionManager = connectionManager;
        _players = new ConcurrentDictionary<string, Player>();
        _npcs = new ConcurrentDictionary<string, Uriel>();
        _connectionManager.PlayerConnected += OnPlayerConnected;
        _connectionManager.PlayerDisconnected += OnPlayerDisconnected;
        _connectionManager.PlayerReconnected += OnPlayerReconnected;
        _connectionManager.PlayerTimedOut += OnPlayerTimedOut;
        
        _map = new ServerMap("Tutorial");

        _npcs.TryAdd("Uriel", new Uriel());
    }

    public async void Start()
    {
        _logger.LogInformation("Starting game loop");

#pragma warning disable CS4014
        Task.Run(BroadcastLoop);
#pragma warning restore CS4014
        
        var previousTime = DateTime.UtcNow;

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var currentTime = DateTime.UtcNow;
                var deltaTime = currentTime - previousTime;
                previousTime = currentTime;
        
                Update(deltaTime);

                await Task.Delay(50);
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
                        entity.Velocity = new Vector2(0, 0);
                        //entity.InvertDirection();
                    }
                }
            }
            
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
            _logger.LogInformation("Player {PlayerId} joined the world", connection.Id);
            
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
            _logger.LogInformation("Player {PlayerId} disconnected from the world", connection.Id);
            
            var packet = SPlayerDisconnectedPacket.Create(connection.Id);
            
            await BroadcastToOthers(connection.Id, packet);
        }
        else
        {
            _logger.LogError("Failed to remove player {PlayerId} form the world", connection.Id);
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
                player.Character.IsChatting,
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
    
    private async Task BroadcastToOthers(string except, NetworkPacket packet)
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

    public Task HandleMovementPacket(IRemoteSource source, CPlayerMovementPacket packet)
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
        return Task.CompletedTask;
    }

    public async Task HandleChatMessagePacket(IRemoteSource source, CChatMessagePacket packet)
    {
        var msgPacket = SChatMessagePacket.Create(packet.ClientId, packet.Message, packet.DateTime);
        
        await BroadcastToOthers(packet.ClientId, msgPacket);
    }

    public Task HandleOpenChatPacket(IRemoteSource source, COpenChatPacket packet)
    {
        if (_players.TryGetValue(packet.ClientId, out var player))
        {
            player.Character.IsChatting = true;
        }
        return Task.CompletedTask;
    }

    public Task HandleCloseChatPacket(IRemoteSource source, CCloseChatPacket packet)
    {
        if (_players.TryGetValue(packet.ClientId, out var player))
        {
            player.Character.IsChatting = false;
        }
        return Task.CompletedTask;
    }

    public Task HandleAuthPacket(IRemoteSource source, CAuthPacket packet)
    {
        var client = (TcpClient) source;
        
        _logger.LogDebug("Handling auth packet from {EndPoint}", client.Socket.RemoteEndPoint);
        
        // TODO: Actual authentication logic

        // Generate 256 bits private key for this client
        var privateKey = new byte[32]; // 256 bits = 32 bytes
        using (var rng = new RNGCryptoServiceProvider())
        {
            rng.GetBytes(privateKey);
        }

        var response = SAuthResultPacket.Create(packet.Username, true, "OK", privateKey);
        
        return _packetSerializer.SerializeToNetwork(client.Stream, response);
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
