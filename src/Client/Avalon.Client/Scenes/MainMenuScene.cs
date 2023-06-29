using System;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Client.Managers;
using Avalon.Client.Models;
using Avalon.Client.Network;
using Avalon.Client.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Scenes;

public class MainMenuScene : Scene
{
    private TextInputComponent _textInputComponent;
    private ButtonComponent _buttonComponent;
    private Sprite _logo;
    private Sprite _inputError;
    private Sprite _inputOk;
    private bool _isInputErrorVisible;
    private bool _isInputOkVisible;

    public MainMenuScene(SceneManager sceneManager) : base(sceneManager)
    {
    }

    public override void Load()
    {
        _logo = new Sprite(
            Globals.Content.Load<Texture2D>("Images/Logo"), 
            new Vector2(Globals.WindowSize.X / 2f, 100)
        );
        
        _textInputComponent = new TextInputComponent(
            new Vector2(100, 160),
            new Vector2(200, 40),
            2,
            Globals.Content.Load<SpriteFont>("Fonts/Default")
        )
        {
            AllowAlphabetic = true,
            IsFocused = true
        };
        
        _inputError = new Sprite(
            Globals.Content.Load<Texture2D>("Images/UI/checkbox_cross"), 
            new Vector2(320, 180)
        );
        _inputOk = new Sprite(
            Globals.Content.Load<Texture2D>("Images/UI/checkbox_ok"), 
            new Vector2(320, 180)
        );
        _isInputErrorVisible = false;
        _isInputOkVisible = false;
        
        _buttonComponent = new ButtonComponent(
            Globals.Content.Load<Texture2D>("Images/Label"),
            new Vector2(100, 220),
            Color.Red,
            Color.Yellow
        );
        
        _textInputComponent.OnPressedEnter += OnLoginButtonClicked;
        _textInputComponent.OnTextChanged += OnLoginInputChanged;
        _buttonComponent.Clicked += OnLoginButtonClicked;
    }

    public override void Unload()
    {
        
    }

    public override void Update(GameTime gameTime)
    {
        MouseManager.Instance.Update();

        _buttonComponent?.Update();
        
        _textInputComponent?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(sortMode: SpriteSortMode.FrontToBack, blendState: BlendState.AlphaBlend);
        
        _logo.Draw(spriteBatch);
        _textInputComponent?.Draw(spriteBatch);
        if (_isInputErrorVisible)
        {
            _inputError.Draw(spriteBatch);
        }
        if (_isInputOkVisible)
        {
            _inputOk.Draw(spriteBatch);
        }
        _buttonComponent?.Draw(spriteBatch);
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logo?.Dispose();
            _textInputComponent?.Dispose();
            _buttonComponent?.Dispose();
        }
    }
    
    private void OnLoginInputChanged(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3 || text.Length > 8)
        {
            _isInputErrorVisible = true;
            _isInputOkVisible = false;
            return;
        }

        _isInputErrorVisible = false;
        _isInputOkVisible = true;
    }
    
    private async void OnLoginButtonClicked(object sender)
    {
        var username = _textInputComponent.Text;

        if (string.IsNullOrEmpty(username) || username.Length < 3 || username.Length > 8)
        {
            _isInputErrorVisible = true;
            _isInputOkVisible = false;
            return;
        }
        
        _isInputOkVisible = true;
        _isInputErrorVisible = false;

        Globals.ClientId = username;
        
        await UdpEnetClient.Instance.SendWelcomePacket();
        Thread.Sleep(200);
        await TcpClient.Instance.SendWelcomePacket();
        
        SceneManager.LoadScene(nameof(TutorialScene));
        
        Console.WriteLine("Loaded tutorial scene. Name: " + Globals.ClientId);
    }
}
