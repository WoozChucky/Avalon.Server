using System;
using System.Diagnostics;
using Avalon.Client.Managers;
using Avalon.Client.Network;
using Avalon.Client.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Avalon.Client;

public class AvalonGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch;
    private readonly SceneManager _sceneManager;

    private readonly Stopwatch _stopwatch;
    
    private volatile bool _showingMetrics = false;

    public AvalonGame(Guid clientId)
    {
        this._stopwatch = Stopwatch.StartNew();
        _graphics = new GraphicsDeviceManager(this);
        _graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";

        Window.Title = "Avalon: The Beginning";
        
        UdpEnetClient.Instance.LatencyUpdated += ((sender, latency) =>
        {
            Window.Title = $"Avalon: The Beginning ({latency}ms)";
        });
        
        //TargetElapsedTime = TimeSpan.FromSeconds(1d / 60d);
        //MaxElapsedTime = TimeSpan.FromMilliseconds(500);
        InactiveSleepTime = TimeSpan.Zero;
        IsMouseVisible = true;
        IsFixedTimeStep = false;        
        
        _sceneManager = new SceneManager();
        _sceneManager.AddScene(nameof(MainMenuScene), new MainMenuScene(_sceneManager));
        _sceneManager.AddScene(nameof(TutorialScene), new TutorialScene(_sceneManager));
    }

    protected override void Initialize()
    {
        // 64 cols x 48 rows
        Globals.WindowSize = new Point(800, 600);
        _graphics.PreferredBackBufferWidth = Globals.WindowSize.X;
        _graphics.PreferredBackBufferHeight = Globals.WindowSize.Y;
        _graphics.GraphicsDevice.Viewport = new Viewport(0, 0, Globals.WindowSize.X, Globals.WindowSize.Y);

        _graphics.ApplyChanges();

        Globals.Content = Content;
        Globals.GraphicsDevice = GraphicsDevice;

        TcpClient.Instance.ConnectAsync().GetAwaiter().GetResult();
        UdpEnetClient.Instance.ConnectAsync().GetAwaiter().GetResult();
        
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
        InputManager.Instance.Update(deltaTime);

        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.LeftAlt))
        {
            UdpEnetClient.Instance.Disconnect();
            Exit();
        }

        Globals.Update(gameTime);
        
        //if (InputManager.Instance.KeyPressed(Keys.F2))
        //{
        //    _showingMetrics = !_showingMetrics;
        //}

        _sceneManager.Update(gameTime);
        
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
