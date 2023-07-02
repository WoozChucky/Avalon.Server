using System;
using System.Threading;
using Avalon.Client.Managers;
using Avalon.Client.Network;
using Avalon.Network.Packets.Auth;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI.MainMenu;

public delegate void LoginFailedEventHandler(string message);
public delegate void LoginSuccessEventHandler(string username);
public delegate void RegisterClickedEventHandler();

public class LoginForm : IGameComponent
{
    private readonly TextInputComponent _usernameComponent;
    private readonly TextInputComponent _passwordComponent;
    private readonly ButtonComponent _loginButton;
    private readonly ButtonComponent _registerButton;

    private bool _isVisible;
    
    public event LoginFailedEventHandler LoginFailed;
    public event LoginSuccessEventHandler LoginSuccess;
    public event RegisterClickedEventHandler RegisterClicked;
    
    public LoginForm(bool visible)
    {
        _isVisible = visible;
        
        TcpClient.Instance.AuthResult += OnAuthResult;
        
        _usernameComponent = new TextInputComponent(
            new Vector2(Globals.WindowSize.X / 2f - 200 /2f, Globals.WindowSize.Y / 2f + 0),
            new Vector2(200, 40),
            2,
            Globals.Content.Load<SpriteFont>("Fonts/Default"),
            "Wooz"
        )
        {
            AllowAlphabetic = true,
            IsFocused = true
        };
        _usernameComponent.OnTabPressed += OnUsernameTabPressed;
        _usernameComponent.OnTextChanged += OnUsernameInputChanged;
        
        _passwordComponent = new TextInputComponent(
            new Vector2(Globals.WindowSize.X / 2f - 200 /2f, Globals.WindowSize.Y / 2f + 50),
            new Vector2(200, 40),
            2,
            Globals.Content.Load<SpriteFont>("Fonts/Default"),
            "123"
        )
        {
            AllowAlphabetic = true,
            AllowNumeric = true,
            AllowPunctuation = true,
            IsSecret = true,
            IsFocused = false
        };
        _passwordComponent.OnTabPressed += OnPasswordTabPressed;
        _passwordComponent.OnTextChanged += OnPasswordInputChanged;
        _passwordComponent.OnPressedEnter += OnLoginButtonClicked;
        

        var loginButtonTexture = Globals.Content.Load<Texture2D>("Images/Label");

        _loginButton = new ButtonComponent(
            loginButtonTexture,
            new Vector2(Globals.WindowSize.X / 2f - (float) loginButtonTexture.Width / 2, Globals.WindowSize.Y / 2f + 100),
            "Login",
            Globals.Content.Load<SpriteFont>("Fonts/Default"),
            Color.Red,
            Color.Yellow
        );
        
        _loginButton.Clicked += OnLoginButtonClicked;
        
        var registerButtonTexture = Globals.Content.Load<Texture2D>("Images/Label");
        
        _registerButton = new ButtonComponent(
            registerButtonTexture,
            new Vector2(Globals.WindowSize.X / 2f - (float) registerButtonTexture.Width / 2, Globals.WindowSize.Y / 2f + 150),
            "Register New Acc",
            Globals.Content.Load<SpriteFont>("Fonts/Default"),
            Color.Red,
            Color.Yellow
        );
        
        _registerButton.Clicked += OnRegisterButtonClicked;
    }

    #region Lifecycle

    public void Update(float deltaTime)
    {
        if (!_isVisible)
        {
            return;
        }
        
        _usernameComponent?.Update(deltaTime);
        _passwordComponent?.Update(deltaTime);
        
        _loginButton?.Update();
        _registerButton?.Update();
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!_isVisible)
        {
            return;
        }
        
        _usernameComponent?.Draw(spriteBatch);
        _passwordComponent?.Draw(spriteBatch);
        _loginButton?.Draw(spriteBatch);
        _registerButton?.Draw(spriteBatch);
    }

    public void ToggleVisibility()
    {
        _isVisible = !_isVisible;
    }
    
    public void Dispose()
    {
        _usernameComponent?.Dispose();
        _passwordComponent?.Dispose();
        _loginButton?.Dispose();
        _registerButton?.Dispose();
    }
    
    #endregion
    
    private void OnAuthResult(object sender, SAuthResultPacket packet)
    {
        if (packet.Success)
        {
            //TODO: Also add private key from packet to udp client for data encryption
            UdpEnetClient.Instance.SetPrivateKey(packet.PrivateKey);
            LoginSuccess?.Invoke(_usernameComponent.Text);
        }
        else
        {
            LoginFailed?.Invoke(packet.Message);
        }
    }
    
    private void OnPasswordTabPressed()
    {
        _usernameComponent.IsFocused = true;
        _passwordComponent.IsFocused = false;
    }
    
    private void OnUsernameTabPressed()
    {
        _usernameComponent.IsFocused = false;
        _passwordComponent.IsFocused = true;
    }
    
    private bool OnUsernameInputChanged(string text)
    {
        // Validator function for username field
        if (string.IsNullOrEmpty(text) || text.Length < 3 || text.Length > 8)
        {
            return false;
        }
        return true;
    }
    
    private bool OnPasswordInputChanged(string text)
    {
        // Validator function for password field
        if (string.IsNullOrEmpty(text) || text.Length < 3 || text.Length > 16) // TODO: Add enforcement of special characters and digits
        {
            return false;
        }
        return true;
    }
    
    private async void OnLoginButtonClicked(object sender)
    {
        var username = _usernameComponent.Text;
        var password = _passwordComponent.Text;

        if (!OnUsernameInputChanged(username) || !OnPasswordInputChanged(password))
        {
            // validation failed
            // TODO: Show error message
            return;
        }

        // Send tcp packet to server with login information
        await TcpClient.Instance.SendAuthPacket(username, password);
    }
    
    

    private void OnRegisterButtonClicked(ButtonComponent button)
    {
        RegisterClicked?.Invoke();
    }

    
}
