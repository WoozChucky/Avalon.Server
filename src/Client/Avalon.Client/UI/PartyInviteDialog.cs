using System;
using Avalon.Client.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class PartyInviteDialog : IDisposable
{

    public bool Active { get; set; }
    
    private SpriteFont titleFont;
    private SpriteFont contentFont;
    private Texture2D backgroundTexture;
    private Vector2 position;
    private string title;
    private string content;
    private ButtonComponent acceptButton;
    private ButtonComponent cancelButton;
    private int padding = 10;

    public PartyInviteDialog(
        SpriteFont titleFont, 
        SpriteFont contentFont,
        Vector2 position, 
        string title, 
        string content
        )
    {
        this.titleFont = titleFont;
        this.contentFont = contentFont;
        this.position = position;
        this.title = title;
        this.content = content;
        
        CreateBackgroundTexture();

        acceptButton = new ButtonComponent(
            Globals.Content.Load<Texture2D>("Images/Icons/Check1"),
            new Vector2(backgroundTexture.Bounds.X + backgroundTexture.Width, backgroundTexture.Bounds.Y + backgroundTexture.Height),
            null,
            null,
            true,
            Globals.Content.Load<Texture2D>("Images/Icons/Check")
        );
        //acceptButton.Clicked += OnAcceptClicked;
    }
    
    public void Update(float deltaTime)
    {
        if (!Active)
        {
            return;
        }

        acceptButton.Update();
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        if (!Active)
        {
            return;
        }
        
        // Draw the background
        spriteBatch.Draw(backgroundTexture, position + Globals.CameraPosition, Color.White);

        // Draw the title
        Vector2 titlePosition = position + new Vector2(20, 20);
        spriteBatch.DrawString(titleFont, title, titlePosition + Globals.CameraPosition, Color.White);

        // Draw the content
        Vector2 contentPosition = titlePosition + new Vector2(0, 40);
        spriteBatch.DrawString(contentFont, content, contentPosition + Globals.CameraPosition, Color.White);

        // Draw the accept button
        acceptButton.Draw(spriteBatch);
        
        // Draw the cancel button
        //spriteBatch.Draw(buttonTexture, cancelButtonRect, isCancelButtonPressed ? Color.Gray : Color.White);
    }

    public void Dispose()
    {
        backgroundTexture?.Dispose();
        acceptButton?.Dispose();
    }

    private void CreateBackgroundTexture()
    {
        var titleTextSize = titleFont.MeasureString(title);
        var contentTextSize = contentFont.MeasureString(content);
        
        var width = (int) (contentTextSize.X + 2 * padding);
        var height = (int) (titleTextSize.Y + 2 * padding + contentTextSize.Y + 2 * padding + 40);
        
        const int borderWidth = 2;
        
        backgroundTexture = new Texture2D(Globals.GraphicsDevice, width, height);
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
        
        backgroundTexture.SetData(backgroundColor);
        
        //_backgroundPosition = Vector2.Zero;
    }
}
