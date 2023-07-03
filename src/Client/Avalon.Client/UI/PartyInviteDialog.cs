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
    private Texture2D buttonTexture;
    private Vector2 position;
    private string title;
    private string content;
    private Rectangle acceptButtonRect;
    private Rectangle cancelButtonRect;
    private bool isHoveringAcceptButton;
    private bool isHoveringCancelButton;
    private bool isAcceptButtonPressed;
    private bool isCancelButtonPressed;
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
        CreateButtonTexture();

        int buttonWidth = buttonTexture.Width;
        int buttonHeight = buttonTexture.Height;
        int buttonSpacing = 10;
        int buttonAreaWidth = 2 * buttonWidth + buttonSpacing;
        int buttonAreaHeight = buttonHeight;
        int buttonAreaX = (int)position.X + (backgroundTexture.Width - buttonAreaWidth) / 2;
        int buttonAreaY = (int)position.Y + backgroundTexture.Height - buttonAreaHeight - 20;

        acceptButtonRect = new Rectangle(buttonAreaX, buttonAreaY, buttonWidth, buttonHeight);
        cancelButtonRect = new Rectangle(buttonAreaX + buttonWidth + buttonSpacing, buttonAreaY, buttonWidth, buttonHeight);
    }
    
    public void Update(float deltaTime)
    {
        if (!Active)
        {
            return;
        }

        acceptButtonRect.X = (int) (acceptButtonRect.X + Globals.CameraPosition.X);
        acceptButtonRect.Y = (int) (acceptButtonRect.Y + Globals.CameraPosition.Y);
        
        cancelButtonRect.X = (int) (cancelButtonRect.X + Globals.CameraPosition.X);
        cancelButtonRect.Y = (int) (cancelButtonRect.Y + Globals.CameraPosition.Y);
        
        var mousePosition = InputManager.Instance.MousePosition;

        isHoveringAcceptButton = acceptButtonRect.Contains(mousePosition);
        isHoveringCancelButton = cancelButtonRect.Contains(mousePosition);

        if (isHoveringAcceptButton && InputManager.Instance.IsLeftButtonClicked())
        {
            isAcceptButtonPressed = true;
        }
        else if (isHoveringCancelButton && InputManager.Instance.IsLeftButtonClicked())
        {
            isCancelButtonPressed = true;
        }
        else
        {
            isAcceptButtonPressed = false;
            isCancelButtonPressed = false;
        }
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
        spriteBatch.Draw(buttonTexture, acceptButtonRect, isAcceptButtonPressed ? Color.Gray : Color.White);
        
        // Draw the cancel button
        spriteBatch.Draw(buttonTexture, cancelButtonRect, isCancelButtonPressed ? Color.Gray : Color.White);
    }

    public void Dispose()
    {
        backgroundTexture?.Dispose();
        buttonTexture?.Dispose();
    }
    
    private void CreateButtonTexture()
    {
        var width = 80;
        var height = 40;
        
        const int borderWidth = 2;
        
        buttonTexture = new Texture2D(Globals.GraphicsDevice, width, height);
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
        
        buttonTexture.SetData(backgroundColor);
        
        //_backgroundPosition = Vector2.Zero;
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
