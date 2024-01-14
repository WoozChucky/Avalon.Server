using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalon.Client.Managers;
using Avalon.Client.Scenes;
using Avalon.Network.Tcp;
using Avalon.Network.Udp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Steamworks;

namespace Avalon.Client;

public class AvalonGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private readonly SceneManager _sceneManager;

    private readonly Stopwatch _stopwatch;
    
    private volatile bool _showingMetrics = false;
    
    private float _elapsedTime;
    private int _frameCount;
    private int _fps;

    protected override void OnExiting(object sender, EventArgs args)
    {
        Console.WriteLine("Exiting...");
        base.OnExiting(sender, args);
    }
    
    protected override void OnActivated(object sender, EventArgs args)
    {
        Console.WriteLine("Activated...");
        base.OnActivated(sender, args);
    }

    protected override void OnDeactivated(object sender, EventArgs args)
    {
        Console.WriteLine("Deactivated...");
        base.OnDeactivated(sender, args);
    }

    public AvalonGame()
    {
        this._stopwatch = Stopwatch.StartNew();
        _graphics = new GraphicsDeviceManager(this);
        _graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";

        Window.AllowAltF4 = false;

        Window.Title = "Avalon: The Beginning";
        Window.FileDrop += (sender, args) =>
        {
            var filePath = args.Files[0];
            var fileName = System.IO.Path.GetFileName(filePath);
            var fileExtension = System.IO.Path.GetExtension(filePath);
            if (fileExtension == ".png")
            {
                var screenshot = Texture2D.FromFile(GraphicsDevice, filePath);
                var screenshotPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Avalon", fileName);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(screenshotPath));
                using var stream = System.IO.File.OpenWrite(screenshotPath);
                screenshot.SaveAsPng(stream, screenshot.Width, screenshot.Height);
            }
        };
        
        Globals.Tcp = new AvalonTcpClient(new AvalonTcpClientSettings
        {
            Host = "play.avalon.monster",
            Port = 21000,
            CertificatePath = "cert-public.pem"
        });
        
        //TargetElapsedTime = TimeSpan.FromSeconds(1d / 60d);
        //MaxElapsedTime = TimeSpan.FromMilliseconds(500);
        InactiveSleepTime = TimeSpan.Zero;
        IsMouseVisible = false;
        IsFixedTimeStep = false;
        
        _sceneManager = new SceneManager();
        _sceneManager.AddScene(nameof(MainMenuScene), new MainMenuScene(_sceneManager));
        _sceneManager.AddScene(nameof(CharacterSelectionScene), new CharacterSelectionScene(_sceneManager));
        _sceneManager.AddScene(nameof(TutorialScene), new TutorialScene(_sceneManager));
    }

    protected override void Initialize()
    {
        // 64 cols x 48 rows
        Globals.WindowSize = new Point(1024, 768);
        _graphics.PreferredBackBufferWidth = Globals.WindowSize.X;
        _graphics.PreferredBackBufferHeight = Globals.WindowSize.Y;
        _graphics.GraphicsDevice.Viewport = new Viewport(0, 0, Globals.WindowSize.X, Globals.WindowSize.Y);
        
        _graphics.ApplyChanges();

        Globals.Content = Content;
        Globals.GraphicsDevice = GraphicsDevice;

        Globals.Tcp.ConnectAsync().GetAwaiter().GetResult();

        _sceneManager.Initialize();
        _sceneManager.LoadScene(nameof(MainMenuScene));


        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        _stopwatch.Restart();

        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        _elapsedTime += deltaTime;
        _frameCount++;
        
        InputManager.Instance.Update(deltaTime);

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.F10))
        {
            SteamClient.Shutdown();
            Globals.Tcp.Disconnect();
            Exit();
        }

        Globals.Update(gameTime);

        _sceneManager.Update(gameTime);
        
        if (_elapsedTime >= 1f) // Calculate FPS every second
        {
            _fps = _frameCount;
            _frameCount = 0;
            _elapsedTime = 0;
        }
        
        _stopwatch.Stop();
        if (_showingMetrics)
        {
            Console.WriteLine($"Update took {_stopwatch.ElapsedTicks}ms");
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _stopwatch.Restart();
        GraphicsDevice.Clear(Color.CornflowerBlue);

        _sceneManager.Draw(_spriteBatch);

        _stopwatch.Stop();
        if (_showingMetrics)
        {
            Console.WriteLine($"Draw took {_stopwatch.ElapsedMilliseconds}ms");
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _sceneManager.UnloadContent();
        
        base.UnloadContent();
    }
}
