using System;
using System.Collections.Concurrent;
using Avalon.Client.Managers;
using Avalon.Client.Maps;
using Avalon.Client.Models;
using Avalon.Client.Network;
using Avalon.Client.UI;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Movement;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TcpClient = Avalon.Client.Network.TcpClient;
using Timer = System.Timers.Timer;

namespace Avalon.Client.Scenes;

public class TutorialScene : Scene
{
    private Map _map;
    private Player _player;
    private Matrix _translation;
    private SpriteFont _font;
    private Banner _banner;
    private ChatGUI _chatGui;

    private readonly ConcurrentDictionary<string, OtherPlayer> _otherPlayers;
    private readonly ConcurrentDictionary<string, OtherPlayer> _npcs;

    private Vector2 _lastSentPosition;
    private Vector2 _lastSentVelocity;

    private float _elapsedTime;
    private int _frameCount;
    private int _fps;
    private double _latency;

    public TutorialScene(SceneManager sceneManager) : base(sceneManager)
    {
        _otherPlayers = new ConcurrentDictionary<string, OtherPlayer>();
        _npcs = new ConcurrentDictionary<string, OtherPlayer>();

        Globals.CameraPosition = Vector2.Zero;
        
        TcpClient.Instance.PlayerConnected += OnPlayerConnected;
        TcpClient.Instance.PlayerDisconnected += OnPlayerDisconnected;
        //TcpClient.Instance.PlayerMoved += OnPlayerMoved;
        
        UdpEnetClient.Instance.LatencyUpdated += OnLatencyUpdated;
        UdpEnetClient.Instance.PlayerMoved += OnPlayerMoved;
        UdpEnetClient.Instance.NpcUpdated += OnNpcUpdated;
    }

    public override void Load()
    {
        _font = Globals.Content.Load<SpriteFont>("Fonts/Nintendo");
        _map = new Map("Tutorial", "Serene_Village_32x32");
        _player = new Player(
            Globals.ClientId,
            Globals.Content.Load<Texture2D>("Images/player"), 
            //new Vector2(Globals.WindowSize.X / 2f, Globals.WindowSize.Y / 2f),
            new Vector2(326, 1450)
        );
        _player.SetBounds(_map.MapSize, new Point(_map.TileWidth, _map.TileHeight));
        
        _chatGui = new ChatGUI();

        var t = new Timer();
        t.Interval = 26; // 20 updates per seconds which would be 50 milliseconds interval (1000/20 = 50)
        t.AutoReset = true;
        t.Elapsed += OnTimerElapsed;
        t.Start();

        _banner = new Banner(new Vector2(0, 0), new Vector2(280, 200), alpha: 0.5f);
        _banner.Load();
    }

    public override void Unload()
    {
        // TODO: Unload any content that was loaded for the scene
        _banner.Unload();
    }

    public override void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        _elapsedTime += deltaTime;
        _frameCount++;

        foreach (var (_, otherHero) in _otherPlayers)
        {
            otherHero.Update(deltaTime);
        }
        
        foreach (var (_, npc) in _npcs)
        {
            npc.Update(deltaTime);
        }

        _player.Update(_map.IsObjectColliding, _npcs, _otherPlayers, _chatGui.IsTyping);
        
        // Update the camera position based on player movement or other logic
        Globals.CameraPosition = _player.Position - new Vector2((float) Globals.GraphicsDevice.Viewport.Width / 2,
            (float) Globals.GraphicsDevice.Viewport.Height / 2) + new Vector2(_map.TileWidth / 2f, _map.TileHeight / 2f);
        
        // Perform boundary checks to prevent the camera from going outside the game world
        Globals.CameraPosition = new Vector2(
            MathHelper.Clamp(Globals.CameraPosition.X, 0, _map.MapSize.X - Globals.GraphicsDevice.Viewport.Width),
            MathHelper.Clamp(Globals.CameraPosition.Y, 0, _map.MapSize.Y - Globals.GraphicsDevice.Viewport.Height)
        );
        
        _chatGui?.Update(deltaTime);
        if (_chatGui is { IsTyping: false } && InputManager.Instance.KeyReleased(Keys.T))
        {
            _chatGui?.Toggle();
        }

        if (InputManager.Instance.KeyReleased(Keys.F3))
        {
            _map.ToggleDebug();
        }

        _banner.Update(gameTime);
        
        CalculateTranslation();
        
        if (_elapsedTime >= 1f) // Calculate FPS every second
        {
            _fps = _frameCount;
            _frameCount = 0;
            _elapsedTime = 0;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        /*
        var sortMode = SpriteSortMode.FrontToBack;
        var blendState = BlendState.AlphaBlend;
        */
        spriteBatch.Begin(transformMatrix: _translation);
        _map.Draw(spriteBatch);
        _player.Draw(spriteBatch);
        foreach (var (id, otherHero) in _otherPlayers)
        {
            otherHero.Draw(spriteBatch);
        }
        foreach (var (_, npc) in _npcs)
        {
            npc.Draw(spriteBatch);
        }
        
        _chatGui?.Draw(spriteBatch);

        if (true)
        {
            _banner.Draw(spriteBatch);
            spriteBatch.DrawString(_font, $"X: {Math.Round(_player.Position.X, 1)} Y: {Math.Round(_player.Position.Y, 1)}", new Vector2(3, 2) + Globals.CameraPosition, Color.DarkBlue);
            spriteBatch.DrawString(_font, $"FPS: {_fps}", new Vector2(3, 36) + Globals.CameraPosition, Color.DarkBlue);
            spriteBatch.DrawString(_font, $"Latency: {_latency}ms", new Vector2(3, 62) + Globals.CameraPosition, Color.DarkBlue);
        }

        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _map?.Dispose();
            _player?.Dispose();
            _chatGui?.Dispose();
        }
    }

    private void CalculateTranslation()
    {
        var dx = (Globals.WindowSize.X / 2f) - _player.Position.X;
        dx = MathHelper.Clamp(dx, -_map.MapSize.X + Globals.WindowSize.X + (_map.TileWidth / 2), _map.TileWidth / 2f);
        var dy = (Globals.WindowSize.Y / 2f) - _player.Position.Y;
        dy = MathHelper.Clamp(dy, -_map.MapSize.Y + Globals.WindowSize.Y + (_map.TileHeight / 2), _map.TileHeight / 2f);
        _translation = Matrix.CreateTranslation(dx, dy, 0f);
    }
    
    private void OnPlayerConnected(object sender, SPlayerConnectedPacket packet)
    {
        _otherPlayers.TryAdd(packet.ClientId, new OtherPlayer(packet.ClientId,Globals.Content.Load<Texture2D>("Images/player"), new Vector2(0, 0)));
    }
    
    private void OnPlayerDisconnected(object sender, SPlayerDisconnectedPacket packet)
    {
        _otherPlayers.TryRemove(packet.ClientId, out _);
    }
    
    private void OnPlayerMoved(object sender, SPlayerPositionUpdatePacket packet)
    {
        if (_otherPlayers.TryGetValue(packet.ClientId, out var player))
        {
            player.OnMovementReceived(new Vector2(packet.PositionX, packet.PositionY), new Vector2(packet.VelocityX, packet.VelocityY), _latency);
            player.OnChatChanged(packet.Chatting);
        }

        if (packet.ClientId == Globals.ClientId)
        {
            if (_player != null)
            {
                // _hero.UpdateVelocity(new Vector2(packet.VelocityX, packet.VelocityY));
            }
        }
    }
    private void OnNpcUpdated(object sender, SNpcUpdatePacket packet)
    {
        _npcs.AddOrUpdate(packet.Id, id =>
            {
                // If the NPC does not already exist in the dictionary, create a new OtherPlayer object and return it.
                return new OtherPlayer(
                    packet.Id,
                    Globals.Content.Load<Texture2D>("Images/player"), new Vector2(packet.PositionX, packet.PositionY), true);
            }, 
            (guid, player) =>
            {
                // If the NPC already exists, just update its position and velocity and return it.
                player.OnMovementReceived(new Vector2(packet.PositionX, packet.PositionY), new Vector2(packet.VelocityX, packet.VelocityY), _latency);
                return player;
            });
    }

    private void OnLatencyUpdated(object sender, double latency)
    {
        _latency = latency;
    }
    
    private async void OnTimerElapsed(object sender, EventArgs e)
    {
        
        { // Send movement updates if the player has moved
            if (_player.Position == _lastSentPosition && _player.Velocity == _lastSentVelocity) return;
        
            await UdpEnetClient.Instance.BroadcastMovementUpdates(Globals.Time, _player.Position.X, _player.Position.Y, _player.Velocity.X, _player.Velocity.Y);
            _lastSentPosition = _player.Position;
            _lastSentVelocity = _player.Velocity;
        }
        
    }
}
