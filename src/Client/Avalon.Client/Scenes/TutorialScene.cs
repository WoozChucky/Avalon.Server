using System;
using System.Collections.Concurrent;
using Avalon.Client.Managers;
using Avalon.Client.Maps;
using Avalon.Client.Models;
using Avalon.Client.Network;
using Avalon.Client.UI;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Social;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using TcpClient = Avalon.Client.Network.TcpClient;
using Timer = System.Timers.Timer;

namespace Avalon.Client.Scenes;

public class TutorialScene : Scene
{
    private Map _map;
    private Player _player;
    private Matrix _translation;
    private SpriteFont _font;

    private ChatGUI _chatGui;
    private InGameRightPanel _rightPanel;
    private Cursor _cursor;
    
    private PartyInviteDialog _partyInviteDialog;

    private readonly ConcurrentDictionary<int, OtherPlayer> _otherPlayers;
    private readonly ConcurrentDictionary<int, OtherPlayer> _npcs;
    
    private Song _song;

    private Vector2 _lastSentPosition;
    private Vector2 _lastSentVelocity;

    private float _elapsedTime;
    private int _frameCount;
    private int _fps;
    private double _latency;

    private volatile bool _loaded = false;

    public TutorialScene(SceneManager sceneManager) : base(sceneManager)
    {
        _otherPlayers = new ConcurrentDictionary<int, OtherPlayer>();
        _npcs = new ConcurrentDictionary<int, OtherPlayer>();

        Globals.CameraPosition = Vector2.Zero;
        
        TcpClient.Instance.PlayerConnected += OnPlayerConnected;
        TcpClient.Instance.PlayerDisconnected += OnPlayerDisconnected;
        TcpClient.Instance.GroupInvite += OnPlayerGroupInvited;
        TcpClient.Instance.PlayerMoved += OnPlayerMoved;
        TcpClient.Instance.NpcUpdated += OnNpcUpdated;
        
        UdpEnetClient.Instance.PlayerMoved += OnPlayerMoved;
        UdpEnetClient.Instance.NpcUpdated += OnNpcUpdated;
    }

    public override void Load()
    {
        _song = Globals.Content.Load<Song>("Music/CitySound");
        MediaPlayer.Volume = 0.015f;
        MediaPlayer.Play(_song);
        _font = Globals.Content.Load<SpriteFont>("Fonts/Nintendo");
        _map = new Map("Tutorial", "Serene_Village_32x32");
        _player = new Player(
            Globals.AccountId,
            "Nuno",
            //Globals.CharacterName,
            Globals.Content.Load<Texture2D>("Images/player"), 
            //new Vector2(Globals.WindowSize.X / 2f, Globals.WindowSize.Y / 2f),
            new Vector2(326, 1450)
        );
        _player.SetBounds(_map.MapSize, new Point(_map.TileWidth, _map.TileHeight));
        
        _chatGui = new ChatGUI();
        _rightPanel = new InGameRightPanel();
        _cursor = new Cursor(Globals.Content.Load<Texture2D>("Images/Icons/Mouse"));

        var t = new Timer();
        t.Interval = 26; // 20 updates per seconds which would be 50 milliseconds interval (1000/20 = 50)
        t.AutoReset = true;
        t.Elapsed += OnTimerElapsed;
        t.Start();

        UdpEnetClient.Instance.LatencyUpdated += OnLatencyUpdated;
        //UdpEnetClient.Instance.PlayerMoved += OnPlayerMoved;
        //UdpEnetClient.Instance.NpcUpdated += OnNpcUpdated;
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
        
        _cursor?.Update(deltaTime);

        foreach (var (_, otherHero) in _otherPlayers)
        {
            otherHero.Update(deltaTime);
        }
        
        foreach (var (_, npc) in _npcs)
        {
            npc.Update(deltaTime);
        }

        _player?.Update(_map.IsObjectColliding, _npcs, _otherPlayers, _chatGui.IsTyping);
        
        // Update the camera position based on player movement or other logic
        Globals.CameraPosition = _player.Position - new Vector2((float) Globals.GraphicsDevice.Viewport.Width / 2,
            (float) Globals.GraphicsDevice.Viewport.Height / 2) + new Vector2(_map.TileWidth / 2f, _map.TileHeight / 2f);
        
        // Perform boundary checks to prevent the camera from going outside the game world
        Globals.CameraPosition = new Vector2(
            MathHelper.Clamp(Globals.CameraPosition.X, 0, _map.MapSize.X - Globals.GraphicsDevice.Viewport.Width),
            MathHelper.Clamp(Globals.CameraPosition.Y, 0, _map.MapSize.Y - Globals.GraphicsDevice.Viewport.Height)
        );
        
        _partyInviteDialog?.Update(deltaTime);
        
        _chatGui?.Update(deltaTime);
        if (_chatGui is { IsTyping: false } && InputManager.Instance.KeyReleased(Keys.T))
        {
            _chatGui?.Toggle();
        }
        
        _rightPanel?.Update(deltaTime);
        if (_rightPanel is not null && InputManager.Instance.KeyPressed(Keys.P))
        {
            _rightPanel?.ToggleVisibility();
        }

        if (InputManager.Instance.KeyReleased(Keys.F3))
        {
            _map.ToggleDebug();
        }

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
        
        _partyInviteDialog?.Draw(spriteBatch);
        
        _chatGui?.Draw(spriteBatch);
        _rightPanel?.Draw(spriteBatch);

        if (true)
        {
            spriteBatch.DrawString(_font, $"X: {Math.Round(_player.Position.X, 1)} Y: {Math.Round(_player.Position.Y, 1)}", new Vector2(3, 2) + Globals.CameraPosition, Color.DarkBlue);
            spriteBatch.DrawString(_font, $"FPS: {_fps}", new Vector2(3, 36) + Globals.CameraPosition, Color.DarkBlue);
            spriteBatch.DrawString(_font, $"Latency: {_latency}ms", new Vector2(3, 62) + Globals.CameraPosition, Color.DarkBlue);
        }
        
        _cursor.Draw(spriteBatch);

        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            MediaPlayer.Stop();
            _map?.Dispose();
            _player?.Dispose();
            _chatGui?.Dispose();
            _rightPanel?.Dispose();
            _song?.Dispose();
            _cursor?.Dispose();
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
        _otherPlayers.TryAdd(packet.AccountId, new OtherPlayer(packet.AccountId, packet.CharacterId, packet.Name, Globals.Content.Load<Texture2D>("Images/player"), new Vector2(0, 0)));
    }
    
    private void OnPlayerDisconnected(object sender, SPlayerDisconnectedPacket packet)
    {
        _otherPlayers.TryRemove(packet.AccountId, out _);
    }
    
    private void OnPlayerMoved(object sender, SPlayerPositionUpdatePacket packet)
    {
        if (_otherPlayers.TryGetValue(packet.AccountId, out var player))
        {
            player.OnMovementReceived(new Vector2(packet.PositionX, packet.PositionY), new Vector2(packet.VelocityX, packet.VelocityY), _latency);
            player.OnChatChanged(packet.Chatting);
        }

        if (packet.AccountId == Globals.AccountId)
        {
            if (_player != null)
            {
                // _hero.UpdateVelocity(new Vector2(packet.VelocityX, packet.VelocityY));
            }
        }
    }
    
    private void OnPlayerGroupInvited(object sender, SGroupInvitePacket packet)
    {
        _partyInviteDialog = new PartyInviteDialog(
            Globals.Content.Load<SpriteFont>("Fonts/Default"),
            Globals.Content.Load<SpriteFont>("Fonts/Default"),
            new Vector2(175f, 150f),
            "Party Invite",
            $"You have been invited by {packet.InviterName} to join a party."
        );
        _partyInviteDialog.Active = true;
    }
    
    private void OnNpcUpdated(object sender, SNpcUpdatePacket packet)
    {
        _npcs.AddOrUpdate(packet.Id, id =>
            {
                // If the NPC does not already exist in the dictionary, create a new OtherPlayer object and return it.
                var texture = Globals.Content.Load<Texture2D>("Images/player");
                return new OtherPlayer(
                    packet.Id,
                    packet.Id,
                    packet.Name,
                    texture, new Vector2(packet.PositionX, packet.PositionY), true);
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
