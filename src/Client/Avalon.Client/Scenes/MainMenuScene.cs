using System;
using Avalon.Client.Managers;
using Avalon.Client.Models;
using Avalon.Client.UI;
using Avalon.Client.UI.MainMenu;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Steamworks;

namespace Avalon.Client.Scenes;

public class MainMenuScene : Scene
{
    private Sprite _logo;
    private Sprite _background;
    
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
            new Vector2(Globals.WindowSize.X / 2f, 150)
        );
        
        _background = new Sprite(
            Globals.Content.Load<Texture2D>("Images/LoginBackground"), 
            new Vector2(Globals.WindowSize.X / 2f, Globals.WindowSize.Y / 2f)
        );
        
        _cursor = new Cursor(Globals.Content.Load<Texture2D>("Images/Icons/Mouse"), false);
        
        _loginForm = new LoginForm(true);
        _loginForm.LoginSuccess += OnLoginSuccess;
        _loginForm.LoginFailed += OnLoginFailed;
        _loginForm.RegisterClicked += OnRegisterFormClicked;
        
        _registerForm = new RegisterForm();

        if (!SteamFriends.SetRichPresence("status", "In Main Menu"))
        {
            Console.WriteLine("Failed to set rich presence");
        }
    }

    public override void Unload()
    {
        _loginForm.LoginSuccess -= OnLoginSuccess;
        _loginForm.LoginFailed -= OnLoginFailed;
        _loginForm.RegisterClicked -= OnRegisterFormClicked;
    }

    public override void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        if (_isLoggedIn)
        {
            _isLoggedIn = false;
            SceneManager.LoadScene(nameof(CharacterSelectionScene));
        
            Console.WriteLine("Loaded char selection scene");
            return;
        }

        _cursor?.Update(deltaTime);
        _loginForm?.Update(deltaTime);
        _registerForm?.Update(deltaTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(blendState: BlendState.AlphaBlend);
        _background.Draw(spriteBatch);
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
            _background?.Dispose();
            _logo?.Dispose();
            _loginForm?.Dispose();
            _registerForm?.Dispose();
            _cursor?.Dispose();
            
            Console.WriteLine("MainMenuScene disposed");
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

    private void OnLoginSuccess(int accountId)
    {
        _loginForm?.ToggleVisibility();
        
        Console.WriteLine("Logged in. Account ID: " + accountId);
        
        Globals.AccountId = accountId;
        Globals.Tcp.AccountId = accountId;

        _isLoggedIn = true;
    }
    
    #endregion
    
}
