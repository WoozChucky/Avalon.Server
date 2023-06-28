using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Avalon.Client.UI;

public class TextInputComponent
{
    private string currentText;
    private Vector2 position;
    private Vector2 size;
    private int cursorIndex;
    private Texture2D rectangleTexture;
    private SpriteFont font;

    public TextInputComponent(Vector2 position, Vector2 size, SpriteFont font)
    {
        this.position = position;
        this.size = size;
        currentText = string.Empty;
        cursorIndex = 0;
        this.font = font;
        
        // Create a single-pixel texture for drawing rectangles
        rectangleTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        rectangleTexture.SetData(new Color[] { Color.White });
    }
    
    public void Update()
    {
        // Retrieve the current keyboard state
        KeyboardState keyboardState = Keyboard.GetState();

        // Handle keyboard input
        Keys[] pressedKeys = keyboardState.GetPressedKeys();
        if (pressedKeys.Length > 0)
        {
            foreach (Keys key in pressedKeys)
            {
                // Handle special keys
                if (key == Keys.Back && currentText.Length > 0 && cursorIndex > 0)
                {
                    // Delete the character before the cursor
                    currentText = currentText.Remove(cursorIndex - 1, 1);
                    cursorIndex--;
                }
                else if (key == Keys.Delete && currentText.Length > 0 && cursorIndex < currentText.Length)
                {
                    // Delete the character after the cursor
                    currentText = currentText.Remove(cursorIndex, 1);
                }
                else if (key == Keys.Left && cursorIndex > 0)
                {
                    // Move the cursor to the left
                    cursorIndex--;
                }
                else if (key == Keys.Right && cursorIndex < currentText.Length)
                {
                    // Move the cursor to the right
                    cursorIndex++;
                }
                else
                {
                    // Append the pressed key to the current text at the cursor position
                    currentText = currentText.Insert(cursorIndex, key.ToString());
                    cursorIndex++;
                }
            }
        }
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        // Draw the text input component
        spriteBatch.Draw(rectangleTexture, new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y), Color.White);
        spriteBatch.DrawString(font, currentText, position, Color.Black);

        // Draw the cursor
        Vector2 cursorPosition = position + new Vector2(font.MeasureString(currentText.Substring(0, cursorIndex)).X, 0);
        spriteBatch.Draw(rectangleTexture, new Rectangle((int)cursorPosition.X, (int)cursorPosition.Y, 1, (int)size.Y), Color.Black);
    }
}
