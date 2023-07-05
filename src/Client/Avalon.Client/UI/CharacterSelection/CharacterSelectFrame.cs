
using System;
using Avalon.Client.Network;
using Avalon.Network.Packets.Character;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI.CharacterSelection;

public delegate void CharacterSelectHandler(CharacterInfo info);
public delegate void CharacterDeleteHandler(CharacterInfo info);

public class CharacterSelectFrame
{
    private Vector2 _position;
    private Vector2 _size;
    
    private ButtonComponent _selectButton;
    private ButtonComponent _deleteButton;
    
    private SpriteFont _nameFont;
    private Vector2 _namePosition;
    
    private SpriteFont _levelFont;
    private Vector2 _levelPosition;
    private string _levelText;
    
    private Texture2D _backgroundTexture;
    
    private CharacterInfo _characterInfo;
    
    public event CharacterSelectHandler Selected;
    public event CharacterDeleteHandler Deleted;
    
    public CharacterSelectFrame(Vector2 position, CharacterInfo info)
    {
        _characterInfo = info;
        _position = position;
        _size = new Vector2(200, 100);
        
        _nameFont = Globals.Content.Load<SpriteFont>("Fonts/Default");
        _namePosition = new Vector2(
            _position.X + 10,
            _position.Y + 10
        );
        
        _levelFont = Globals.Content.Load<SpriteFont>("Fonts/Default");
        _levelPosition = new Vector2(
            _position.X + 10,
            _position.Y + 30
        );
        _levelText = $"Level {_characterInfo.Level}";
        
        CreateBackgroundTexture();
        
        _selectButton = new ButtonComponent(
            Globals.Content.Load<Texture2D>("Images/Icons/Check1"),
            new Vector2(_position.X + 10, _position.Y + 60),
            null,
            null,
            Globals.Content.Load<Texture2D>("Images/Icons/Check")
        );
        _selectButton.Clicked += OnSelectButtonClicked;
        
        _deleteButton = new ButtonComponent(
            Globals.Content.Load<Texture2D>("Images/Icons/Trash1"),
            new Vector2(_position.X + 110, _position.Y + 60),
            null,
            null,
            Globals.Content.Load<Texture2D>("Images/Icons/Trash")
        );
        _deleteButton.Clicked += OnDeleteButtonClicked;
    }


    public void Update(float deltaTime)
    {
        _selectButton?.Update();
        _deleteButton?.Update();
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Draw(_backgroundTexture, _position, Color.White);
        
        spriteBatch.DrawString(_nameFont, _characterInfo.Name, _namePosition, Color.White);
        spriteBatch.DrawString(_levelFont, _levelText, _levelPosition, Color.White);
        
        _selectButton?.Draw(spriteBatch);
        _deleteButton?.Draw(spriteBatch);
    }
    
    private void OnDeleteButtonClicked(ButtonComponent button)
    {
        Deleted?.Invoke(_characterInfo);
    }

    private void OnSelectButtonClicked(ButtonComponent button)
    {
        Selected?.Invoke(_characterInfo);
    }
    
    private void CreateBackgroundTexture()
    {
        var width = (int) _size.X;
        var height = (int) _size.Y;
        
        const int borderWidth = 2;
        
        _backgroundTexture = new Texture2D(Globals.GraphicsDevice, width, height);
        var backgroundColor = new Color[width * height];
        
        var bgColor = new Color(195, 172, 144, 255);
        var borderColor = new Color(181, 117, 77);
        
        // Fill the entire texture with a semi-transparent red color
        for (var i = 0; i < backgroundColor.Length; i++)
        {
            backgroundColor[i] = bgColor;
        }

        // Set the top and bottom border pixels to black
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < borderWidth; y++)
            {
                var topIndex = y * width + x;
                var bottomIndex = (height - 1 - y) * width + x;
                backgroundColor[topIndex] = borderColor;
                backgroundColor[bottomIndex] = borderColor;
            }
        }
        
        // Set the left and right border pixels to black
        for (var y = borderWidth; y < height - borderWidth; y++)
        {
            for (var x = 0; x < borderWidth; x++)
            {
                var leftIndex = y * width + x;
                var rightIndex = y * width + (width - 1 - x);
                backgroundColor[leftIndex] = borderColor;
                backgroundColor[rightIndex] = borderColor;
            }
        }
        
        _backgroundTexture.SetData(backgroundColor);
    }
}
