using System;
using System.Collections.Concurrent;
using Avalon.Client.Managers;
using Avalon.Client.Maps;
using Avalon.Client.Models;
using Avalon.Client.Network;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Movement;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TcpClient = Avalon.Client.Network.TcpClient;
using Timer = System.Timers.Timer;

namespace Avalon.Client.Scenes;

public struct InterpolatedPlayerPosition
{
    public float X { get; set; }
    public float Y { get; set; }
    public float InterpolationTime { get; set; }
    public float ElapsedTime { get; set; }

    public float PreviousX { get; set; }
    public float PreviousY { get; set; }

    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
}

public class TutorialScene : Scene
{
    private Map _map;
    private Hero _hero;
    private Matrix _translation;
    private SpriteFont _font;

    private readonly ConcurrentDictionary<Guid, Player> _otherPlayers;
    private readonly ConcurrentDictionary<Guid, InterpolatedPlayerPosition> _interpolatedPlayerPositions;

    private float _elapsedTime;
    private int _frameCount;
    private int _fps;
    private double _latency;
    
    private SceneManager _sceneManager;

    public TutorialScene(SceneManager sceneManager) : base(sceneManager)
    {
        _otherPlayers = new ConcurrentDictionary<Guid, Player>();
        _interpolatedPlayerPositions = new ConcurrentDictionary<Guid, InterpolatedPlayerPosition>();
        
        Globals.CameraPosition = Vector2.Zero;
        
        TcpClient.Instance.PlayerConnected += OnPlayerConnected;
        TcpClient.Instance.PlayerDisconnected += OnPlayerDisconnected;
        //TcpClient.Instance.PlayerMoved += OnPlayerMoved;
        
        UdpClient.Instance.LatencyUpdated += OnLatencyUpdated;
        UdpClient.Instance.PlayerMoved += OnPlayerMoved;
    }

    private void OnLatencyUpdated(object sender, double latency)
    {
        _latency = latency;
    }

    public override void Load()
    {
        _font = Globals.Content.Load<SpriteFont>("Fonts/Nintendo");
        _map = new Map("Tutorial", "Serene_Village_32x32");
        _hero = new Hero(
            Globals.Content.Load<Texture2D>("Images/player"), 
            //new Vector2(Globals.WindowSize.X / 2f, Globals.WindowSize.Y / 2f),
            new Vector2(326, 1450)
        );
        _hero.SetBounds(_map.MapSize, new Point(_map.TileWidth, _map.TileHeight));

        Timer t = new Timer();
        t.Interval = 50; // 20 updates per second, could try 10 updates which would be 50 milliseconds
        t.AutoReset = true;
        t.Elapsed += (sender, args) =>
        {
            //TcpClient.Instance.BroadcastMovementUpdates(Globals.Time, _hero.Position.X, _hero.Position.Y, InputManager.Direction.X, InputManager.Direction.Y);
            UdpClient.Instance.BroadcastMovementUpdates(Globals.Time, _hero.Position.X, _hero.Position.Y, InputManager.Direction.X, InputManager.Direction.Y).GetAwaiter().GetResult();
        };
        t.Start();

    }

    public override void Unload()
    {
        // TODO: Unload any content that was loaded for the scene
    }

    public override void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        _elapsedTime += deltaTime;
        _frameCount++;
        
        // TODO: Update the scene's logic
        InputManager.Update();
        
        foreach (var (_, otherHero) in _otherPlayers)
        {
            otherHero.Update(deltaTime);
        }

        _hero.Update(_map.IsObjectColliding);
        

        // Update the camera position based on player movement or other logic
        Globals.CameraPosition = _hero.Position - new Vector2(Globals.GraphicsDevice.Viewport.Width / 2,
            Globals.GraphicsDevice.Viewport.Height / 2) + new Vector2(_map.TileWidth / 2f, _map.TileHeight / 2f);
        
        // Perform boundary checks to prevent the camera from going outside the game world
        Globals.CameraPosition = new Vector2(
            MathHelper.Clamp(Globals.CameraPosition.X, 0, _map.MapSize.X - Globals.GraphicsDevice.Viewport.Width),
            MathHelper.Clamp(Globals.CameraPosition.Y, 0, _map.MapSize.Y - Globals.GraphicsDevice.Viewport.Height)
        );
        
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
        spriteBatch.Begin(transformMatrix: _translation);
        _map.Draw(spriteBatch);
        _hero.Draw(spriteBatch);
        foreach (var (id, otherHero) in _otherPlayers)
        {
            otherHero.Draw(spriteBatch);
        }
        spriteBatch.DrawString(_font, $"X: {Math.Round(_hero.Position.X, 2)} Y: {Math.Round(_hero.Position.Y, 2)}", new Vector2(10, 10) + Globals.CameraPosition, Color.DarkBlue);
        spriteBatch.DrawString(_font, $"FPS: {_fps}", new Vector2(10, 36) + Globals.CameraPosition, Color.DarkBlue);
        spriteBatch.DrawString(_font, $"Latency: {_latency}ms", new Vector2(10, 62) + Globals.CameraPosition, Color.DarkBlue);
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _map?.Dispose();
            _hero?.Dispose();
        }
    }

    private void CalculateTranslation()
    {
        var dx = (Globals.WindowSize.X / 2f) - _hero.Position.X;
        dx = MathHelper.Clamp(dx, -_map.MapSize.X + Globals.WindowSize.X + (_map.TileWidth / 2), _map.TileWidth / 2f);
        var dy = (Globals.WindowSize.Y / 2f) - _hero.Position.Y;
        dy = MathHelper.Clamp(dy, -_map.MapSize.Y + Globals.WindowSize.Y + (_map.TileHeight / 2), _map.TileHeight / 2f);
        _translation = Matrix.CreateTranslation(dx, dy, 0f);
    }
    
    private void OnPlayerConnected(object sender, SPlayerConnectedPacket packet)
    {
        _otherPlayers.TryAdd(packet.ClientId, new Player(Globals.Content.Load<Texture2D>("Images/player"), new Vector2(0, 0)));
    }
    
    private void OnPlayerDisconnected(object sender, SPlayerDisconnectedPacket packet)
    {
        _otherPlayers.TryRemove(packet.ClientId, out _);
    }
    
    private void OnPlayerMoved(object sender, SPlayerPositionUpdatePacket packet)
    {
        if (_otherPlayers.TryGetValue(packet.ClientId, out var player))
        {
            player.UpdateVelocity(new Vector2(packet.VelocityX, packet.VelocityY));
            player.UpdatePosition(new Vector2(packet.PositionX, packet.PositionY), packet.Elapsed);
        }
    }
}
