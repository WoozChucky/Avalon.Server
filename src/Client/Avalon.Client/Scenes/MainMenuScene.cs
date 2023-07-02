using System;
using System.Threading;
using Avalon.Client.Managers;
using Avalon.Client.Models;
using Avalon.Client.Network;
using Avalon.Client.UI;
using Avalon.Client.UI.MainMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Scenes;

public class MainMenuScene : Scene
{
    private Sprite _logo;
    
    private RegisterForm _registerForm;
    private LoginForm _loginForm;
    private Cursor _cursor;

    private volatile bool _isLoggedIn;

    public MainMenuScene(SceneManager sceneManager) : base(sceneManager)
    {
        _isLoggedIn = false;
    }

    #region Lifecycle

    public override void Load()
    {
        _logo = new Sprite(
            Globals.Content.Load<Texture2D>("Images/Logo"), 
            new Vector2(Globals.WindowSize.X / 2f, 100)
        );
        
        _cursor = new Cursor(Globals.Content.Load<Texture2D>("Images/Icons/Mouse"));
        
        _loginForm = new LoginForm(true);
        _loginForm.LoginSuccess += OnLoginSuccess;
        _loginForm.LoginFailed += OnLoginFailed;
        _loginForm.RegisterClicked += OnRegisterFormClicked;
        
        _registerForm = new RegisterForm();
    }

    public override void Unload()
    {
        
    }

    public override async void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        _cursor?.Update(deltaTime);
        _loginForm?.Update(deltaTime);
        _registerForm?.Update(deltaTime);
        
        if (_isLoggedIn)
        {
            _isLoggedIn = false;
            await UdpEnetClient.Instance.SendWelcomePacket();
            Thread.Sleep(200);
            await TcpClient.Instance.SendWelcomePacket();
        
            SceneManager.LoadScene(nameof(TutorialScene));
        
            Console.WriteLine("Loaded tutorial scene. Name: " + Globals.ClientId);
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(blendState: BlendState.AlphaBlend);
        _logo.Draw(spriteBatch);
        _loginForm?.Draw(spriteBatch);
        _registerForm?.Draw(spriteBatch);
        _cursor?.Draw(spriteBatch);
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logo?.Dispose();
            _loginForm?.Dispose();
            _registerForm?.Dispose();
            _cursor?.Dispose();
        }
    }
    
    #endregion

    #region Event Handlers
    
    private void OnRegisterFormClicked()
    {
        _loginForm?.ToggleVisibility();
        _registerForm?.ToggleVisibility();
    }

    private void OnLoginFailed(string message)
    {
        // Show dialog with message
    }

    private void OnLoginSuccess(string username)
    {
        _loginForm?.ToggleVisibility();
        
        Globals.ClientId = username;
        
        _isLoggedIn = true;
    }
    
    #endregion
    
}
