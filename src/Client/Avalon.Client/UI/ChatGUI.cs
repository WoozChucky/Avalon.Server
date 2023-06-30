using System;
using System.Collections.Generic;
using Avalon.Client.Managers;
using Avalon.Client.Network;
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
    private Texture2D _backgroundTexture;
    private Vector2 _backgroundPosition;
    
    private SpriteFont _messageFont;
    
    public ChatGUI()
    {
        _visible = false;
        _chatMessages = new List<string>();
        _messageFont = Globals.Content.Load<SpriteFont>("Fonts/File");
        
        var inputPosition = new Vector2(
            Globals.WindowSize.X - 400, 
            Globals.WindowSize.Y - 60
        );
        var inputSize = new Vector2(300, 40);
        
        _textInputComponent = new TextInputComponent(inputPosition, inputSize, 2, Globals.Content.Load<SpriteFont>("Fonts/Default"), true);
        _textInputComponent.OnTextChanged += OnTextChanged;
        _textInputComponent.OnPressedEnter += OnPressedEnter;
        _textInputComponent.AllowAlphabetic = true;
        _textInputComponent.AllowNumeric = true;
        _textInputComponent.AllowSpace = true;
        CreateBackgroundTexture();
        
        TcpClient.Instance.ChatMessage += OnChatMessageReceived;
    }

    private void OnChatMessageReceived(object sender, SChatMessagePacket packet)
    {
        _chatMessages.Add($"[{packet.ClientId}]: {packet.Message}");
    }

    public void Update(float deltaTime)
    {
        _textInputComponent.Update(deltaTime);

        if (IsTyping && InputManager.Instance.KeyPressed(Keys.Escape))
        {
            _textInputComponent.IsFocused = false;
            _visible = false;
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (!_visible)
            return;

        // Draw chat background
        spriteBatch.Draw(_backgroundTexture, Globals.CameraPosition + _backgroundPosition, Color.White);

        // Draw chat messages
        float messageY = Globals.CameraPosition.Y + (_backgroundPosition.Y) /* starting Y position for the messages */;
        foreach (string message in _chatMessages)
        {
            spriteBatch.DrawString(_messageFont, message, new Vector2(Globals.CameraPosition.X + _backgroundPosition.X + 20, messageY), Color.Black);
            messageY += _messageFont.MeasureString(message).Y + 2 /* vertical spacing between messages */;
        }

        // Draw current message being typed
        //string promptText = "Type your message...";
        //spriteBatch.DrawString(/* font */, promptText, new Vector2(/* messageX */, /* messageY for current message */), Color.Gray);
        //spriteBatch.DrawString(/* font */, _currentMessage, new Vector2(/* messageX */, /* messageY for current message */), Color.White);
        _textInputComponent.Draw(spriteBatch);
    }
    
    public void Toggle()
    {
        _visible = !_visible;
        if (_visible)
        {
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
        const int height = 120;
        const int borderWidth = 2;
        
        _backgroundTexture = new Texture2D(Globals.GraphicsDevice, width, height);
        var backgroundColor = new Color[width * height];
        
        // Fill the entire texture with a semi-transparent red color
        for (var i = 0; i < backgroundColor.Length; i++)
        {
            backgroundColor[i] = new Color(180, 0, 0, 60);
        }

        // Set the top and bottom border pixels to black
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < borderWidth; y++)
            {
                var topIndex = y * width + x;
                var bottomIndex = (height - 1 - y) * width + x;
                //backgroundColor[topIndex] = Color.Black;
                backgroundColor[bottomIndex] = Color.Black;
            }
        }
        
        // Set the left and right border pixels to black
        for (var y = borderWidth; y < height - borderWidth; y++)
        {
            for (var x = 0; x < borderWidth; x++)
            {
                var leftIndex = y * width + x;
                var rightIndex = y * width + (width - 1 - x);
                backgroundColor[leftIndex] = Color.Black;
                backgroundColor[rightIndex] = Color.Black;
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
        _chatMessages.Add($"[{Globals.ClientId}]: {message}");
        _textInputComponent.Clear();
        await TcpClient.Instance.SendChatMessage(message);
    }
}
