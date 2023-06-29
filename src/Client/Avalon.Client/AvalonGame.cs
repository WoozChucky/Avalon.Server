using System;
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

    public AvalonGame(Guid clientId)
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.SynchronizeWithVerticalRetrace = true;
        Content.RootDirectory = "Content";


        Window.Title = "Avalon";
        
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60d);
        MaxElapsedTime = TimeSpan.FromMilliseconds(500);
        InactiveSleepTime = TimeSpan.Zero;
        IsMouseVisible = true;
        IsFixedTimeStep = false;        
        
        _sceneManager = new SceneManager();
        _sceneManager.AddScene(nameof(MainMenuScene), new MainMenuScene(_sceneManager));
        _sceneManager.AddScene(nameof(TutorialScene), new TutorialScene(_sceneManager));
    }

    protected override async void Initialize()
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
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            Keyboard.GetState().IsKeyDown(Keys.Escape))
        {
            UdpEnetClient.Instance.Disconnect();
            Exit();
        }
            
        
        Globals.Update(gameTime);

        _sceneManager.Update(gameTime);
        
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);

        _sceneManager.Draw(_spriteBatch);

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _sceneManager.UnloadContent();
        
        base.UnloadContent();
    }
}
