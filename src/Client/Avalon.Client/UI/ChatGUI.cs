using System;
using System.Collections.Generic;
using Avalon.Client.Managers;
using Avalon.Client.Models;
using Avalon.Network.Packets.Social;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Avalon.Client.UI;

public class ChatGUI : IDisposable
{
    public bool IsTyping => _textInputComponent.IsFocused;

    private volatile bool _visible;
    private List<string> _chatMessages;

    private TextInputComponent _textInputComponent;
    
    private CameraFollowingSprite _closeButton;
    private Rectangle _closeButtonBounds;
    
    private Texture2D _backgroundTexture;
    private Vector2 _backgroundPosition;
    
    private SpriteFont _messageFont;
    
    public ChatGUI()
    {
        _visible = false;
        _chatMessages = new List<string>();
        _messageFont = Globals.Content.Load<SpriteFont>("Fonts/ArialSmall");
        
        var inputPosition = new Vector2(
            Globals.WindowSize.X - 400, 
            Globals.WindowSize.Y - 60
        );
        var inputSize = new Vector2(300, 40);
        
        _textInputComponent = new TextInputComponent(inputPosition, inputSize, 2, Globals.Content.Load<SpriteFont>("Fonts/Default"),string.Empty, true);
        _textInputComponent.OnTextChanged += OnTextChanged;
        _textInputComponent.OnPressedEnter += OnPressedEnter;
        _textInputComponent.AllowAlphabetic = true;
        _textInputComponent.AllowNumeric = true;
        _textInputComponent.AllowSpace = true;
        _textInputComponent.AllowPunctuation = true;
        CreateBackgroundTexture();
        
        _closeButton = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Cross"),
            new Vector2(0, 0)
        );
        _closeButton.Position = new Vector2(
            Globals.WindowSize.X - _closeButton.Texture.Width - 5,
            (Globals.WindowSize.Y - _backgroundTexture.Height) + _closeButton.Texture.Height / 2f
        );
        _closeButtonBounds = new Rectangle(
            (int) _closeButton.Position.X,
            (int) _closeButton.Position.Y,
            _closeButton.Texture.Width,
            _closeButton.Texture.Height
        );

        Globals.Tcp.ChatMessage += OnChatMessageReceived;
    }

    private void OnChatMessageReceived(object sender, SChatMessagePacket packet)
    {
        _chatMessages.Add($"[{packet.CharacterName}]: {packet.Message}");
    }

    public void Update(float deltaTime)
    {
        _textInputComponent.Update(deltaTime);
        _closeButton.Update(deltaTime);

        if (IsTyping && InputManager.Instance.KeyPressed(Keys.Escape))
        {
            CloseChat();
        }
        
        if (_visible && InputManager.Instance.IsLeftButtonClicked() && InputManager.Instance.IsMouseOverRectangle(_closeButtonBounds))
        {
            CloseChat();
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!_visible)
            return;

        // Draw chat background
        spriteBatch.Draw(_backgroundTexture, Globals.CameraPosition + _backgroundPosition, Color.White);
        
        // Draw close button
        _closeButton.Draw(spriteBatch);

        // Draw chat messages
        var messageY = Globals.CameraPosition.Y + _backgroundPosition.Y + 5; /* starting Y position for the messages */;
        var shadowPosition = new Vector2(Globals.CameraPosition.X + _backgroundPosition.X + 10, messageY) + new Vector2(2, 2);
        foreach (var message in _chatMessages)
        {
            spriteBatch.DrawString(_messageFont, message, shadowPosition, Color.Black);
            spriteBatch.DrawString(_messageFont, message, new Vector2(Globals.CameraPosition.X + _backgroundPosition.X + 10, messageY), Color.White);
            messageY += _messageFont.MeasureString(message).Y + 2 /* vertical spacing between messages */;
            shadowPosition = new Vector2(Globals.CameraPosition.X + _backgroundPosition.X + 10, messageY) + new Vector2(2, 2);
        }

        _textInputComponent.Draw(spriteBatch);
    }
    
    public async void Toggle()
    {
        _visible = !_visible;
        if (_visible)
        {
            await Globals.Tcp.SendOpenChatPacket();
            _textInputComponent.IsFocused = true;
        }
    }
    
    public void Dispose()
    {
        
    }
    
    private void OnPressedEnter(TextInputComponent button)
    {
        var valid = OnTextChanged(button.Text);
        if (!valid)
            return;
        
        SendChatMessage(button.Text);
    }

    private bool OnTextChanged(string text)
    {
        return !string.IsNullOrEmpty(text) && text.Length > 0;
    }

    private void CreateBackgroundTexture()
    {
        const int width = 400;
        const int height = 200;
        const int borderWidth = 2;
        
        _backgroundTexture = new Texture2D(Globals.GraphicsDevice, width, height);
        var backgroundColor = new Color[width * height];
        
        // Fill the entire texture with a semi-transparent red color
        for (var i = 0; i < backgroundColor.Length; i++)
        {
            backgroundColor[i] = new Color(128, 96, 0, 200);
        }

        // Set the top and bottom border pixels to black
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < borderWidth; y++)
            {
                var topIndex = y * width + x;
                var bottomIndex = (height - 1 - y) * width + x;
                backgroundColor[topIndex] = new Color(204, 153, 0);
                backgroundColor[bottomIndex] = new Color(204, 153, 0);
            }
        }
        
        // Set the left and right border pixels to black
        for (var y = borderWidth; y < height - borderWidth; y++)
        {
            for (var x = 0; x < borderWidth; x++)
            {
                var leftIndex = y * width + x;
                var rightIndex = y * width + (width - 1 - x);
                backgroundColor[leftIndex] = new Color(204, 153, 0);
                backgroundColor[rightIndex] = new Color(204, 153, 0);
            }
        }
        
        _backgroundTexture.SetData(backgroundColor);
        
        _backgroundPosition = new Vector2(
            Globals.WindowSize.X - _backgroundTexture.Width - 10,
            Globals.WindowSize.Y - _backgroundTexture.Height - 10
        );
    }
    
    private async void SendChatMessage(string message)
    {
        _chatMessages.Add($"[{Globals.CharacterName}]: {message}");
        _textInputComponent.Clear();
        await Globals.Tcp.SendChatMessage(message);
    }
    
    private async void CloseChat()
    {
        _textInputComponent.IsFocused = false;
        _textInputComponent.Clear();
        _visible = false;

        await Globals.Tcp.SendCloseChatPacket();
    }
}
