using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Avalon.Client.Managers;
using Avalon.Client.Maps;
using Avalon.Client.Models;
using Avalon.Client.UI;
using Avalon.Common.Extensions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Map;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Social;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Steamworks;
using Color = Microsoft.Xna.Framework.Color;
using Point = Microsoft.Xna.Framework.Point;
using Timer = System.Timers.Timer;

namespace Avalon.Client.Scenes;

public class TutorialScene : Scene
{
    private Map _map;
    private Minimap _minimap;
    private Player _player;
    private SpriteFont _font;

    private ChatGUI _chatGui;
    private InGameRightPanel _rightPanel;
    private Cursor _cursor;
    
    private PartyInviteDialog _partyInviteDialog;

    private ConcurrentDictionary<int, OtherPlayer> _otherPlayers;
    private ConcurrentDictionary<Guid, GameCreature> _npcs;
    
    private Timer _timer;
    
    private Song _song;

    private Vector2 _lastSentPosition;
    private Vector2 _lastSentVelocity;
    
    private double _latency;

    private volatile bool _loaded = false;
    private volatile bool _loggedOut = false;
    private volatile bool _reloadRequired = false;

    public TutorialScene(SceneManager sceneManager) : base(sceneManager)
    {
        
    }

    private async Task Load(MapInfo map)
    {
        _loaded = false;
        
        _otherPlayers = new ConcurrentDictionary<int, OtherPlayer>();
        _npcs = new ConcurrentDictionary<Guid, GameCreature>();

        Globals.CameraPosition = Vector2.Zero;

        _song = Globals.Content.Load<Song>("Music/CitySound");
        MediaPlayer.Volume = 0.015f;
        // MediaPlayer.Play(_song);
        _font = Globals.Content.Load<SpriteFont>("Fonts/Nintendo");
        //_map = new Map(mapName, directory, atlas);
        _map = new Map(map.Data.ToMemoryStream(), map.TilesetsData.Select(m => m.ToMemoryStream()).ToArray(), map.Atlas);
        _minimap = new Minimap();
        _minimap.Load(_map);
        _minimap.Generate();
        _player = new Player(
            Globals.AccountId,
            Globals.CharacterName,
            Globals.Content.Load<Texture2D>("Images/player"),
            new Vector2(Globals.StartPosition.X, Globals.StartPosition.Y)
        );
        _player.SetBounds(_map.MapSize, new Point(_map.TileWidth, _map.TileHeight));
        
        _chatGui = new ChatGUI();
        _rightPanel = new InGameRightPanel();
        _cursor = new Cursor(Globals.Content.Load<Texture2D>("Images/Icons/Mouse"), true);
        
        Globals.Tcp.PlayerConnected += OnPlayerConnected;
        Globals.Tcp.PlayerDisconnected += OnPlayerDisconnected;
        Globals.Tcp.GroupInvite += OnPlayerGroupInvited;
        Globals.Tcp.PlayerMoved += OnPlayerMoved;
        Globals.Tcp.NpcUpdated += OnNpcUpdated;
        Globals.Tcp.Logout += OnLogout;
        Globals.Tcp.MapTeleport += OnMapTeleport;

        _timer = new Timer();
        _timer.Interval = 26; // 20 updates per seconds which would be 50 milliseconds interval (1000/20 = 50)
        _timer.AutoReset = true;
        _timer.Elapsed += OnTimerElapsed;
        _timer.Start();
        
        _loaded = true;

        await Globals.Tcp.SendCharacterLoadedPacket();
        //TODO: Maybe also send a map loaded packet?
        
        SteamFriends.SetRichPresence("status", $"In-Game ({Globals.MapInfo.Description}) (Character: {Globals.CharacterName})");
    }

    public override async void Load()
    {
        Console.WriteLine(Globals.MapInfo);
        await Load(Globals.MapInfo);
        Console.WriteLine("TutorialScene loaded");
    }

    public override void Unload()
    {
        MediaPlayer.Stop();
        RemovePacketHandlers();
        Console.WriteLine("TutorialScene unloaded");
    }

    public override async void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        if (!_loaded)
        {
            return;
        }
        
        if (_loggedOut)
        {
            _loaded = false;
            RemovePacketHandlers();
            SceneManager.LoadScene(nameof(CharacterSelectionScene));
            _loggedOut = false;
            return;
        }

        if (_reloadRequired)
        {
            Console.WriteLine("Reloading map...");
            _reloadRequired = false;
            RemovePacketHandlers();
            await Load(Globals.MapInfo);
            Console.WriteLine("Map reloaded!");
            return;
        }
        
        // Update the camera position based on player movement or other logic
        Globals.CameraPosition = _player.Position - new Vector2((float) Globals.GraphicsDevice.Viewport.Width / 2,
            (float) Globals.GraphicsDevice.Viewport.Height / 2) + new Vector2(_map.TileWidth / 2f, _map.TileHeight / 2f);
        
        // Perform boundary checks to prevent the camera from going outside the game world
        Globals.CameraPosition = new Vector2(
            MathHelper.Clamp(Globals.CameraPosition.X, 0, _map.MapSize.X - Globals.GraphicsDevice.Viewport.Width),
            MathHelper.Clamp(Globals.CameraPosition.Y, 0, _map.MapSize.Y - Globals.GraphicsDevice.Viewport.Height)
        );

        CalculateTranslation();

        _cursor?.Update(deltaTime);
        
        _player?.Update(_map.IsObjectColliding, _npcs, _otherPlayers, _chatGui.IsTyping);

        foreach (var (_, otherHero) in _otherPlayers)
        {
            otherHero.Update(deltaTime);
        }
        
        foreach (var (_, npc) in _npcs)
        {
            npc.Update(deltaTime);
        }
        
        _minimap?.Update(deltaTime);
        if (_chatGui is { IsTyping: false } && InputManager.Instance.KeyPressed(Keys.M))
        {
            _minimap?.ToggleVisibility();
        }

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

        if (_chatGui is { IsTyping: false } && InputManager.Instance.KeyReleased(Keys.O))
        {
            await Globals.Tcp.SendLogoutPacket(Globals.AccountId);
            Console.WriteLine("Sent logout packet at position " + _lastSentPosition);
        }
        
        if (_chatGui is { IsTyping: false } && InputManager.Instance.KeyReleased(Keys.I))
        {
            await Globals.Tcp.SendMapTeleportPacket(Globals.MapInfo.MapId == 1 ? 2 : 1);
            return;
        }
        
        if (_chatGui is { IsTyping: false } && InputManager.Instance.KeyReleased(Keys.E)) // Interact key
        {
            await Globals.Tcp.SendInteractPacket(new System.Drawing.Rectangle(
                _player.InteractBoundingBox.X, 
                _player.InteractBoundingBox.Y, 
                _player.InteractBoundingBox.Width, 
                _player.InteractBoundingBox.Height)
            );
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!_loaded)
        {
            return;
        }
        
        spriteBatch.Begin(transformMatrix: Globals.CameraViewMatrix);
        _map.Draw(spriteBatch);
        foreach (var (_, npc) in _npcs)
        {
            npc.Draw(spriteBatch);
        }
        foreach (var (id, otherHero) in _otherPlayers)
        {
            otherHero.Draw(spriteBatch);
        }
        _player.Draw(spriteBatch);
        
        _partyInviteDialog?.Draw(spriteBatch);
        
        _chatGui?.Draw(spriteBatch);
        _rightPanel?.Draw(spriteBatch);

        _minimap.Draw(spriteBatch);
        
        if (true)
        {
            spriteBatch.DrawString(_font, $"X: {Math.Round(_player.Position.X, 1)} Y: {Math.Round(_player.Position.Y, 1)}", new Vector2(3, 2) + Globals.CameraPosition, Color.DarkBlue);
            spriteBatch.DrawString(_font, $"Latency: {_latency}ms", new Vector2(3, 62) + Globals.CameraPosition, Color.DarkBlue);
        }
        
        _cursor.Draw(spriteBatch);

        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _map?.Dispose();
            _player?.Dispose();
            _chatGui?.Dispose();
            _rightPanel?.Dispose();
            _song?.Dispose();
            _cursor?.Dispose();
            
            Console.WriteLine("TutorialScene disposed");
        }
    }

    private void CalculateTranslation()
    {
        var dx = (Globals.WindowSize.X / 2f) - _player.Position.X;
        dx = MathHelper.Clamp(dx, -_map.MapSize.X + Globals.WindowSize.X + (_map.TileWidth / 2f), _map.TileWidth / 2f);
        var dy = (Globals.WindowSize.Y / 2f) - _player.Position.Y;
        dy = MathHelper.Clamp(dy, -_map.MapSize.Y + Globals.WindowSize.Y + (_map.TileHeight / 2f), _map.TileHeight / 2f);
        Globals.CameraViewMatrix = Matrix.CreateTranslation(dx, dy, 0f);
    }
    
    private void OnPlayerConnected(object sender, SPlayerConnectedPacket packet)
    {
        if (!_loaded) return;
        Console.WriteLine("Player connected: " + packet.Name);
        _otherPlayers.TryAdd(packet.AccountId, new OtherPlayer(packet.AccountId, packet.CharacterId, packet.Name, Globals.Content.Load<Texture2D>("Images/player"), new Vector2(0, 0)));
    }
    
    private void OnPlayerDisconnected(object sender, SPlayerDisconnectedPacket packet)
    {
        Console.WriteLine("Player disconnected: " + packet.AccountId);
        _otherPlayers.TryRemove(packet.AccountId, out _);
    }
    
    private void OnPlayerMoved(object sender, SPlayerPositionUpdatePacket packet)
    {
        if (_otherPlayers.TryGetValue(packet.AccountId, out var player))
        {
            //Console.WriteLine("Received velocity: " + packet.VelocityX + ", " + packet.VelocityY);
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
                return new GameCreature(
                    packet.Id,
                    0,
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
    
    private void OnLogout(object sender, SLogoutPacket packet)
    {
        switch (packet.Result)
        {
            case LogoutResult.Success:
                _loggedOut = true;
                break;
            case LogoutResult.RecentlyInCombat:
                Console.WriteLine("Logout failed: Recently in combat");
                break;
            case LogoutResult.NotInGame:
                Console.WriteLine("Logout failed: Not in game");
                break;
            case LogoutResult.NotSameAccount:
                Console.WriteLine("Logout failed: Not same account");
                break;
            case LogoutResult.InternalError:
                Console.WriteLine("Logout failed: Internal error");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(packet.Result));
        }
    }
    
    private void OnMapTeleport(object sender, SMapTeleportPacket packet)
    {
        Globals.MapInfo = packet.Map;
        Globals.StartPosition = new Vector2(packet.X, packet.Y);
        
        Console.WriteLine(Globals.MapInfo);

        _reloadRequired = true;
    }
    
    private async void OnTimerElapsed(object sender, EventArgs e)
    {
        if (_loggedOut) return;
        
        {   
            // Send movement updates if the player has moved, or if he stopped moving.
            // If position and velocity haven't changed, we don't send an update

            var vel = new Vector2(_player.Velocity.X, _player.Velocity.Y);
            var pos = new Vector2(_player.Position.X, _player.Position.Y);
            
            if ((float.IsNaN(vel.X) && float.IsNaN(vel.Y)) || vel is { X: 0.0f, Y: 0.0f })
            {
                vel = new Vector2(0.0f, 0.0f);
            }
            
            if (pos == _lastSentPosition && vel == _lastSentVelocity)
                return;

            //await Globals.Udp.BroadcastMovementUpdates(Globals.Time, pos.X, pos.Y, vel.X, vel.Y);
            await Globals.Tcp.BroadcastMovementUpdates(Globals.Time, pos.X, pos.Y, vel.X, vel.Y);
            _lastSentPosition = pos;
            _lastSentVelocity = vel;
        }
        
    }

    private void RemovePacketHandlers()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Elapsed -= OnTimerElapsed;
            _timer.Dispose();
        }
        Globals.Tcp.PlayerConnected -= OnPlayerConnected;
        Globals.Tcp.PlayerDisconnected -= OnPlayerDisconnected;
        Globals.Tcp.GroupInvite -= OnPlayerGroupInvited;
        Globals.Tcp.PlayerMoved -= OnPlayerMoved;
        Globals.Tcp.NpcUpdated -= OnNpcUpdated;
        Globals.Tcp.Logout -= OnLogout;
        Globals.Tcp.MapTeleport -= OnMapTeleport;
    }
}
