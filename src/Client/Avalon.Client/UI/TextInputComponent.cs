using System;
using Avalon.Client.Managers;
using Avalon.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Avalon.Client.UI;

public delegate void TextInputPressedEnter(TextInputComponent button);
public delegate bool TextInputChanged(string text);

public class TextInputComponent : IDisposable
{
    private SpriteFont font;
    private readonly bool useCameraPosition;

    private Vector2 position;
    private Vector2 size;
    
    private Texture2D borderTexture;
    private Texture2D rectangleTexture;
    
    private Color backgroundColor;
    private Color borderColor;
    
    private KeyboardState previousKeyboardState;
    
    private int borderWidth;
    
    private string currentText;
    private float verticalOffset;
    
    private const float CursorBlinkInterval = 0.5f; // Cursor blink interval in seconds
    private float cursorBlinkTimer;
    private bool isCursorVisible;
    private int cursorIndex;
    
    private Sprite _inputError;
    private Sprite _inputOk;
    private bool _isInputErrorVisible;
    private bool _isInputOkVisible;

    public event TextInputPressedEnter OnPressedEnter;
    public event TextInputChanged OnTextChanged;
    
    public bool AllowNumeric { get; set; }
    public bool AllowAlphabetic { get; set; }
    public bool AllowSpace { get; set; }
    public bool AllowPunctuation { get; set; }
    public bool IsFocused { get; set; }
    public Rectangle BoundingBox { get; }
    public string Text => currentText;

    public TextInputComponent(Vector2 position, Vector2 size, int borderWidth, SpriteFont font, bool useCameraPosition = false)
    {
        this.position = position;
        this.size = size;
        this.borderWidth = borderWidth;
        this.font = font;
        this.useCameraPosition = useCameraPosition;

        BoundingBox = new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int) size.Y);
        
        currentText = string.Empty;
        
        cursorIndex = 0;
        cursorBlinkTimer = 0f;
        isCursorVisible = true;

        if (useCameraPosition)
        {
            _inputError = new CameraFollowingSprite(
                Globals.Content.Load<Texture2D>("Images/UI/checkbox_cross"), 
                new Vector2(position.X + size.X + 30, position.Y + size.Y / 2f)
            );
            _inputOk = new CameraFollowingSprite(
                Globals.Content.Load<Texture2D>("Images/UI/checkbox_ok"), 
                new Vector2(position.X + size.X + 30, position.Y + size.Y / 2f)
            );
        }
        else
        {
            _inputError = new Sprite(
                Globals.Content.Load<Texture2D>("Images/UI/checkbox_cross"), 
                new Vector2(position.X + size.X + 30, position.Y + size.Y / 2f)
            );
            _inputOk = new Sprite(
                Globals.Content.Load<Texture2D>("Images/UI/checkbox_ok"), 
                new Vector2(position.X + size.X + 30, position.Y + size.Y / 2f)
            );
        }
        
        
        _isInputErrorVisible = false;
        _isInputOkVisible = false;
        
        previousKeyboardState = Keyboard.GetState();
        
        // Create a single-pixel texture for drawing rectangles
        rectangleTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        rectangleTexture.SetData(new Color[] { Color.White });
        
        // Create a single-pixel texture for drawing the border
        borderTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        borderTexture.SetData(new Color[] { Color.Black });
        
        backgroundColor = Color.White;
        borderColor = Color.Red;
        
        CalculateVerticalOffset();
    }
    
    private void CalculateVerticalOffset()
    {
        float textHeight = font.MeasureString(currentText).Y;
        verticalOffset = (size.Y - textHeight) * 0.5f;
    }
    
    public void Update(float deltaTime)
    {
        var isHovered = InputManager.Instance.IsMouseOverRectangle(BoundingBox);

        if (isHovered && InputManager.Instance.IsLeftButtonClicked())
        {
            IsFocused = true;
        }
        else if (!isHovered && InputManager.Instance.IsLeftButtonClicked())
        {
            IsFocused = false;
        }
        
        if (IsFocused)
        {
            HandleKeyboardInput();
            UpdateCursorBlink(deltaTime);
        }
    }
    
    public void Clear()
    {
        currentText = string.Empty;
        cursorIndex = 0;
        CalculateVerticalOffset();
    }
    
    private void HandleKeyboardInput()
    {
        var currentKeyboardState = Keyboard.GetState();
        var pressedKeys = currentKeyboardState.GetPressedKeys();

        foreach (var key in pressedKeys)
        {
            if (previousKeyboardState.IsKeyUp(key))
            {
                if (HandleSpecialKeys(key)) // Delete, Backspace, Left, Right
                    continue;

                if (AllowPunctuation)
                {
                    if (HandlePunctuationKeys(key)) // ., -, etc.
                        continue;
                }

                if (AllowNumeric)
                {
                    if (HandleNumericKeys(key)) // 0-9
                        continue;
                }

                if (AllowAlphabetic)
                {
                    HandleCharacterInput(key); // A-Z, ., etc.
                }
            }
        }
        
        if (font.MeasureString(currentText).X >= size.X - 10)
        {
            cursorIndex--;
            currentText = currentText.Remove(cursorIndex, 1);
        }

        previousKeyboardState = currentKeyboardState;
    }

    private bool HandlePunctuationKeys(Keys key)
    {
        if (key == Keys.D1)
        {
            bool isShiftKeyDown = Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift);
            
            currentText = currentText.Insert(cursorIndex, "!");
            cursorIndex++;
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
            
            return isShiftKeyDown;
        }
        
        if (key == Keys.OemQuotes)
        {
            bool isShiftKeyDown = Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift);
            
            currentText = currentText.Insert(cursorIndex, "?");
            cursorIndex++;
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
            
            return isShiftKeyDown;
        }
        
        if (key == Keys.D7)
        {
            bool isShiftKeyDown = Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift);
            
            currentText = currentText.Insert(cursorIndex, "/");
            cursorIndex++;
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
            
            return isShiftKeyDown;
        }

        return false;
    }

    private bool HandleSpecialKeys(Keys key)
    {
        if (key == Keys.Space && AllowSpace)
        {
            currentText = currentText.Insert(cursorIndex, " ");
            cursorIndex++;
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
            return true;
        }
        
        if (key == Keys.Enter && currentText.Length > 0)
        {
            // Handle Submit action event
            var valid = OnTextChanged?.Invoke(currentText);
            if (valid is true)
            {
                OnPressedEnter?.Invoke(this);
            }
            return true;
        }

        if (key == Keys.Back && currentText.Length > 0 && cursorIndex > 0)
        {
            // Delete the character before the cursor
            currentText = currentText.Remove(cursorIndex - 1, 1);
            cursorIndex--;
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
            return true;
        }

        if (key == Keys.Delete && currentText.Length > 0 && cursorIndex < currentText.Length)
        {
            // Delete the character after the cursor
            currentText = currentText.Remove(cursorIndex, 1);
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
            return true;
        }

        if (key == Keys.Left && cursorIndex > 0)
        {
            // Move the cursor to the left
            cursorIndex--;
            return true;
        }

        if (key == Keys.Right && cursorIndex < currentText.Length)
        {
            // Move the cursor to the right
            cursorIndex++;
            return true;
        }

        return false;
    }

    private bool HandleNumericKeys(Keys key)
    {
        if ((key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.NumPad0 && key <= Keys.NumPad9))
        {
            int number = key >= Keys.D0 && key <= Keys.D9 ? (int)key - (int)Keys.D0 : (int)key - (int)Keys.NumPad0;
            currentText = currentText.Insert(cursorIndex, number.ToString());
            cursorIndex++;
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
            return true;
        }

        return false;
    }

    private void HandleCharacterInput(Keys key)
    {
        char character = GetCharacterFromKey(key);
        if (IsValidCharacter(character))
        {
            string keyRepresentation = character.ToString();
            currentText = currentText.Insert(cursorIndex, keyRepresentation);
            cursorIndex++;
            CalculateVerticalOffset();
            var valid = OnTextChanged?.Invoke(currentText);
            _isInputErrorVisible = valid == false;
            _isInputOkVisible = valid == true;
        }
    }
    
    private bool IsValidCharacter(char character)
    {
        // Define a set of valid characters
        const string validCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-+~@";

        // Check if the character is present in the valid characters set
        return validCharacters.Contains(character);
    }
    
    private char GetCharacterFromKey(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z)
        {
            // Handle letter keys (A-Z)
            bool isShiftKeyDown = Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift);
            return isShiftKeyDown ? (char)key : char.ToLower((char)key);
        }
        else if (key >= Keys.D0 && key <= Keys.D9)
        {
            // Handle number keys (0-9)
            int number = (int)key - (int)Keys.D0;
            return number.ToString()[0];
        }
        else if (key == Keys.OemPeriod)
        {
            // Handle period key (.)
            return '.';
        }
        else if (key is Keys.OemMinus or Keys.OemPlus)
        {
            // Handle minus key (-) or plus key (+)
            return key.ToString()[0];
        }
        else if (key == Keys.OemTilde)
        {
            // Handle tilde key (~)
            return '~';
        }
        else if (key == Keys.RightAlt && Keyboard.GetState().IsKeyDown(Keys.RightControl) && Keyboard.GetState().IsKeyDown(Keys.D2))
        {
            // Handle at key (@)
            return '@';
        }
        // Add additional special characters handling here if needed

        return '\0'; // Return null character for unsupported keys
    }
    
    private void UpdateCursorBlink(float deltaTime)
    {
        cursorBlinkTimer += deltaTime;
        if (cursorBlinkTimer >= CursorBlinkInterval)
        {
            isCursorVisible = !isCursorVisible;
            cursorBlinkTimer = 0f;
        }
    }
    
    public void Draw(SpriteBatch spriteBatch)
    {
        // Draw the background rectangle
        if (useCameraPosition)
        {
            spriteBatch.Draw(rectangleTexture, new Rectangle((int)position.X + (int)Globals.CameraPosition.X, (int)position.Y + (int)Globals.CameraPosition.Y, (int)size.X, (int)size.Y), backgroundColor);
        }
        else
        {
            spriteBatch.Draw(rectangleTexture, new Rectangle((int)position.X, (int)position.Y, (int)size.X, (int)size.Y), backgroundColor);
        }

        // Draw the border
        if (useCameraPosition)
        {
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X + (int) Globals.CameraPosition.X, (int)position.Y + (int) Globals.CameraPosition.Y, (int)size.X, borderWidth), borderColor); // Top border
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X + (int) Globals.CameraPosition.X, (int)position.Y + (int)size.Y - borderWidth + (int) Globals.CameraPosition.Y, (int)size.X, borderWidth), borderColor); // Bottom border
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X + (int) Globals.CameraPosition.X, (int)position.Y + (int) Globals.CameraPosition.Y, borderWidth, (int)size.Y), borderColor); // Left border
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X + (int) size.X - borderWidth + (int) Globals.CameraPosition.X, (int)position.Y + (int) Globals.CameraPosition.Y, borderWidth, (int)size.Y), borderColor); // Right border
        }
        else
        {
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X, (int)position.Y, (int)size.X, borderWidth), borderColor); // Top border
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X, (int)position.Y + (int)size.Y - borderWidth, (int)size.X, borderWidth), borderColor); // Bottom border
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X, (int)position.Y, borderWidth, (int)size.Y), borderColor); // Left border
            spriteBatch.Draw(borderTexture, new Rectangle((int)position.X + (int)size.X - borderWidth, (int)position.Y, borderWidth, (int)size.Y), borderColor); // Right border
        }

        // Draw the current text
        var textPosition = position + new Vector2(borderWidth, verticalOffset);
        textPosition.X += 5; // Add a small horizontal offset to center the text vertically
        
        spriteBatch.DrawString(font, currentText, useCameraPosition ? textPosition + Globals.CameraPosition : textPosition, Color.Black);

        // Draw the cursor
        if (isCursorVisible && IsFocused)
        {
            Vector2 cursorPosition = position + new Vector2(borderWidth) + new Vector2(font.MeasureString(currentText.Substring(0, cursorIndex)).X, 0);
            cursorPosition.X += 5; // Add a small horizontal offset to center the cursor vertically

            if (useCameraPosition)
            {
                cursorPosition.X += Globals.CameraPosition.X;
                cursorPosition.Y += Globals.CameraPosition.Y;
            }
            spriteBatch.Draw(borderTexture, new Rectangle((int)cursorPosition.X, (int)cursorPosition.Y, 1, (int)size.Y - borderWidth), borderColor);
        }
        
        // Draw the input error or input ok message
        if (_isInputErrorVisible)
        {
            _inputError.Draw(spriteBatch);
        }
        if (_isInputOkVisible)
        {
            _inputOk.Draw(spriteBatch);
        }
    }

    public void Dispose()
    {
        if (rectangleTexture != null)
        {
            rectangleTexture.Dispose();
            rectangleTexture = null;
        }

        if (borderTexture != null)
        {
            borderTexture.Dispose();
            borderTexture = null;
        }
        
        if (_inputError != null)
        {
            _inputError.Dispose();
            _inputError = null;
        }

        if (_inputOk != null)
        {
            _inputOk.Dispose();
            _inputOk = null;
        }
    }
}
