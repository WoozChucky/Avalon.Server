using System;
using System.Threading;
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
            new Vector2(Globals.WindowSize.X / 2f - 200 /2f, Globals.WindowSize.Y / 2f + 0),
            new Vector2(200, 40),
            2,
            Globals.Content.Load<SpriteFont>("Fonts/Default")
        )
        {
            AllowAlphabetic = true,
            IsFocused = true
        };

        var buttonTexture = Globals.Content.Load<Texture2D>("Images/Label");

        _buttonComponent = new ButtonComponent(
            buttonTexture,
            new Vector2(Globals.WindowSize.X / 2f - (float) buttonTexture.Width / 2, Globals.WindowSize.Y / 2f + 100),
            "Login",
            Globals.Content.Load<SpriteFont>("Fonts/Default"),
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
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

        _textInputComponent?.Update(deltaTime);
        
        _buttonComponent?.Update();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(blendState: BlendState.AlphaBlend);
        _logo.Draw(spriteBatch);
        _textInputComponent?.Draw(spriteBatch);
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
    
    private bool OnLoginInputChanged(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 3 || text.Length > 8)
        {
            return false;
        }
        return true;
    }
    
    private async void OnLoginButtonClicked(object sender)
    {
        var username = _textInputComponent.Text;

        if (!OnLoginInputChanged(username))
        {
            return;
        }

        Globals.ClientId = username;
        
        await UdpEnetClient.Instance.SendWelcomePacket();
        Thread.Sleep(200);
        await TcpClient.Instance.SendWelcomePacket();
        
        SceneManager.LoadScene(nameof(TutorialScene));
        
        Console.WriteLine("Loaded tutorial scene. Name: " + Globals.ClientId);
    }
}
