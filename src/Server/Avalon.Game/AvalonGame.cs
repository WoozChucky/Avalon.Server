using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using Avalon.Database;
using Avalon.Game.Entities;
using Avalon.Map;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
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
    Task HandleGroupInviteResultPacket(IRemoteSource source, CGroupInviteResultPacket packet);
    Task HandleAuthPatchPacket(IRemoteSource source, CAuthPatchPacket packet);
    Task HandleCharacterSelectedPacket(IRemoteSource source, CCharacterSelectedPacket packet);
    Task HandleCharacterListPacket(IRemoteSource source, CCharacterListPacket packet);
    Task HandleCharacterCreatePacket(IRemoteSource source, CCharacterCreatePacket packet);
    Task HandleCharacterDeletePacket(IRemoteSource source, CCharacterDeletePacket packet);
    Task HandleCharacterLoadedPacket(IRemoteSource source, CCharacterLoadedPacket packet);
    Task HandleLogoutPacket(IRemoteSource source, CLogoutPacket packet);
    
}

public partial class AvalonGame : IAvalonGame
{
    private readonly ILogger<AvalonGame> _logger;
    private readonly CancellationTokenSource _cts;
    private readonly IPacketSerializer _packetSerializer;
    private readonly IAvalonConnectionManager _connectionManager;
    private readonly IDatabaseManager _databaseManager;
    
    private readonly ConcurrentDictionary<int, Uriel> _npcs;
    private readonly ServerMap _map;

    public AvalonGame(ILogger<AvalonGame> logger, 
        IPacketSerializer packetSerializer, 
        IAvalonConnectionManager connectionManager,
        IDatabaseManager databaseManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = new CancellationTokenSource();
        _packetSerializer = packetSerializer;
        _connectionManager = connectionManager;
        _databaseManager = databaseManager;
        _npcs = new ConcurrentDictionary<int, Uriel>();
        
        _connectionManager.SessionLost += OnSessionLost;
        _connectionManager.SessionReconnected += OnPlayerReconnected;
        _connectionManager.SessionTimedOut += OnPlayerTimedOut;
        
        _map = new ServerMap("Tutorial");

        _npcs.TryAdd(1, new Uriel());
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
            
                await Task.Delay(16);
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
            await BroadcastAll(packet);
        }
        
        // Broadcast NPC positions
        foreach (var (id, npc) in _npcs)
        {
            var packet = SNpcUpdatePacket.Create(
                id,
                npc.Name,
                npc.Position.X, 
                npc.Position.Y,
                npc.Velocity.X,
                npc.Velocity.Y
            );
            await BroadcastAll(packet);
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
