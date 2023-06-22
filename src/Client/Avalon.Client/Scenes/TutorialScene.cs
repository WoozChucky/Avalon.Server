using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Client.Managers;
using Avalon.Client.Maps;
using Avalon.Client.Models;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Movement;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProtoBuf;

namespace Avalon.Client.Scenes;

public class TutorialScene : Scene
{
    private Map _map;
    private Hero _hero;
    private Matrix _translation;
    private SpriteFont _font;
    
    private float _elapsedTime;
    private int _frameCount;
    private int _fps;
    
    private SceneManager _sceneManager;
    
    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private readonly X509Certificate2 certificate;
    private readonly Socket socket;
    private SslStream sslStream;

    public TutorialScene(SceneManager sceneManager) : base(sceneManager)
    {
        Globals.CameraPosition = Vector2.Zero;
        
        var clientCertBytes = File.ReadAllBytesAsync("cert-public.pem").ConfigureAwait(true).GetAwaiter().GetResult();
        certificate = new X509Certificate2(clientCertBytes);
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
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
        
        ConnectToServer();
    }

    private async void ConnectToServer()
    {
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 21000));
        sslStream = new SslStream(new NetworkStream(socket), false, UserCertificateValidationCallback);
        await sslStream.AuthenticateAsClientAsync("localhost", new X509Certificate2Collection() { certificate }, SslProtocols.Tls12,
            true);

        Task.Run(HandleCommunications, cts.Token);
        Task.Run(BroadcastMovementUpdates, cts.Token);

        var reqPKeyPacket = new CRequestCryptoKeyPacket();
        using var ms = new MemoryStream();

        Serializer.Serialize(ms, reqPKeyPacket);

        var packet = new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY,
                Flags = NetworkPacketFlags.None,
                Version = 0
            },
            Payload = ms.ToArray()
        };

        Serializer.SerializeWithLengthPrefix(sslStream, packet, PrefixStyle.Base128);
    }

    private async Task BroadcastMovementUpdates()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1000 / 1, cts.Token);
                
                var packet = CPlayerMovementPacket.Create(Globals.Time, _hero.Position.X, _hero.Position.Y);

                Serializer.SerializeWithLengthPrefix(sslStream, packet, PrefixStyle.Base128);
            }
        }
        catch (OperationCanceledException e)
        {

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private async Task HandleCommunications()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(sslStream, PrefixStyle.Base128);

                switch (packet.Header.Type)
                {
                    case NetworkPacketType.SMSG_ENCRYPTION_KEY:
                        var encryptionKeyPacket = Serializer.Deserialize<SCryptoKeyPacket>(new MemoryStream(packet.Payload));
                        Trace.WriteLine($"Received encryption key: {BitConverter.ToString(encryptionKeyPacket.Key)}");
                        break;
                }
            }
        }
        catch (OperationCanceledException e)
        {

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    private bool UserCertificateValidationCallback(object sender, X509Certificate? x509Certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        return true;
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
        spriteBatch.DrawString(_font, $"X: {Math.Round(_hero.Position.X, 2)} Y: {Math.Round(_hero.Position.Y, 2)}", new Vector2(10, 10) + Globals.CameraPosition, Color.DarkBlue);
        spriteBatch.DrawString(_font, $"FPS: {_fps}", new Vector2(10, 36) + Globals.CameraPosition, Color.DarkBlue);
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            sslStream?.Dispose();
            socket?.Dispose();
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
}
