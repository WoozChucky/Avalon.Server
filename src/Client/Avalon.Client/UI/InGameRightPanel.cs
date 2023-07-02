using Avalon.Client.Managers;
using Avalon.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class InGameRightPanel : IGameComponent
{
    private readonly CameraFollowingSprite _smallBackgroundSprite;
    private readonly CameraFollowingSprite _backgroundSprite;
    
    private readonly CameraFollowingSprite _showHideSprite;
    private readonly Rectangle _showHideSpriteBounds;
    private readonly HoverDialog _showHideHoverDialog;
    
    private readonly CameraFollowingSprite _characterSprite;
    private readonly Rectangle _characterSpriteBounds;
    private readonly HoverDialog _characterHoverDialog;

    private readonly CameraFollowingSprite _inventorySprite;
    private readonly Rectangle _inventorySpriteBounds;
    private readonly HoverDialog _inventoryHoverDialog;
    
    private readonly CameraFollowingSprite _socialSprite;
    private readonly Rectangle _socialSpriteBounds;
    private readonly HoverDialog _socialHoverDialog;
    
    private readonly CameraFollowingSprite _questsSprite;
    private readonly Rectangle _questsSpriteBounds;
    private readonly HoverDialog _questsHoverDialog;
    
    private readonly CameraFollowingSprite _settingsSprite;
    private readonly Rectangle _settingsSpriteBounds;
    private readonly HoverDialog _settingsHoverDialog;
    
    private volatile bool _isExpanded;
    
    public InGameRightPanel()
    {
        _isExpanded = false;
        
        _smallBackgroundSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/UI/PanelSmall"),
            new Vector2(0, 0)
        );
        _smallBackgroundSprite.SetAlpha(0.9f);
        _smallBackgroundSprite.Position = new Vector2(
            Globals.WindowSize.X - (float) _smallBackgroundSprite.Texture.Width - 4,
            _smallBackgroundSprite.Texture.Height / 2f + 10
        );
        
        _backgroundSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/UI/Panel"),
            new Vector2(0, 0)
        );
        _backgroundSprite.SetAlpha(0.1f);
        _backgroundSprite.Position = new Vector2(
            Globals.WindowSize.X - (float) _backgroundSprite.Texture.Width - 8,
            _backgroundSprite.Texture.Height / 2f + 10
        );
        
        _showHideSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Menu"),
            new Vector2(0, 0)
        );
        _showHideSprite.Position = new Vector2(
            Globals.WindowSize.X - _showHideSprite.Texture.Width - 20,
            _showHideSprite.Texture.Height / 2f + 20
        );
        _showHideSpriteBounds = new Rectangle(
            (int) _showHideSprite.Position.X,
            (int) _showHideSprite.Position.Y,
            _showHideSprite.Texture.Width,
            _showHideSprite.Texture.Height
        );
        _showHideHoverDialog = new HoverDialog(Globals.Content.Load<SpriteFont>("Fonts/Default"), "(P) Show/Hide Menu");
        
        _characterSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Character"),
            new Vector2(0, 0)
        );
        _characterSprite.Position = new Vector2(
            Globals.WindowSize.X - _characterSprite.Texture.Width - 20,
            _showHideSprite.Position.Y + _characterSprite.Texture.Height + 10
        );
        _characterSpriteBounds = new Rectangle(
            (int) _characterSprite.Position.X,
            (int) _characterSprite.Position.Y,
            _characterSprite.Texture.Width,
            _characterSprite.Texture.Height
        );
        _characterHoverDialog = new HoverDialog(Globals.Content.Load<SpriteFont>("Fonts/Default"), "(C) Character Stats");
        
        _inventorySprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Inventory"),
            new Vector2(0, 0)
        );
        _inventorySprite.Position = new Vector2(
            Globals.WindowSize.X - _inventorySprite.Texture.Width - 20,
            _characterSprite.Position.Y + _inventorySprite.Texture.Height + 10
        );
        _inventorySpriteBounds = new Rectangle(
            (int) _inventorySprite.Position.X,
            (int) _inventorySprite.Position.Y,
            _inventorySprite.Texture.Width,
            _inventorySprite.Texture.Height
        );
        _inventoryHoverDialog = new HoverDialog(Globals.Content.Load<SpriteFont>("Fonts/Default"), "(I) Inventory");
        
        _socialSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Social"),
            new Vector2(0, 0)
        );
        _socialSprite.Position = new Vector2(
            Globals.WindowSize.X - _socialSprite.Texture.Width - 20,
            _inventorySprite.Position.Y + _socialSprite.Texture.Height + 10
        );
        _socialSpriteBounds = new Rectangle(
            (int) _socialSprite.Position.X,
            (int) _socialSprite.Position.Y,
            _socialSprite.Texture.Width,
            _socialSprite.Texture.Height
        );
        _socialHoverDialog = new HoverDialog(Globals.Content.Load<SpriteFont>("Fonts/Default"), "(O) Social");
        
        _questsSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Quests"),
            new Vector2(0, 0)
        );
        _questsSprite.Position = new Vector2(
            Globals.WindowSize.X - _questsSprite.Texture.Width - 20,
            _socialSprite.Position.Y + _questsSprite.Texture.Height + 10
        );
        _questsSpriteBounds = new Rectangle(
            (int) _questsSprite.Position.X,
            (int) _questsSprite.Position.Y,
            _questsSprite.Texture.Width,
            _questsSprite.Texture.Height
        );
        _questsHoverDialog = new HoverDialog(Globals.Content.Load<SpriteFont>("Fonts/Default"), "(L) Quests");
        
        _settingsSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Settings"),
            new Vector2(0, 0)
        );
        _settingsSprite.Position = new Vector2(
            Globals.WindowSize.X - _settingsSprite.Texture.Width - 20,
            _questsSprite.Position.Y + _settingsSprite.Texture.Height + 10
        );
        _settingsSpriteBounds = new Rectangle(
            (int) _settingsSprite.Position.X,
            (int) _settingsSprite.Position.Y,
            _settingsSprite.Texture.Width,
            _settingsSprite.Texture.Height
        );
        _settingsHoverDialog = new HoverDialog(Globals.Content.Load<SpriteFont>("Fonts/Default"), "(F10) Settings");
        
    }

    public void Update(float deltaTime)
    {
        if (!_isExpanded && InputManager.Instance.IsLeftButtonClicked() && InputManager.Instance.IsMouseOverRectangle(_showHideSpriteBounds))
        {
            _isExpanded = true;
        }
        else if (_isExpanded && InputManager.Instance.IsLeftButtonClicked() && InputManager.Instance.IsMouseOverRectangle(_showHideSpriteBounds))
        {
            _isExpanded = false;
        }
        
        _smallBackgroundSprite.Update(deltaTime);
        _backgroundSprite.Update(deltaTime);
        
        _showHideSprite.Update(deltaTime);
        _showHideHoverDialog.Update(deltaTime);
        _showHideHoverDialog.SetVisible(InputManager.Instance.IsMouseOverRectangle(_showHideSpriteBounds));

            _characterSprite.Update(deltaTime);
        _characterHoverDialog.Update(deltaTime);
        if (_isExpanded)
        {
            _characterHoverDialog.SetVisible(InputManager.Instance.IsMouseOverRectangle(_characterSpriteBounds));
        }

        _inventorySprite.Update(deltaTime);
        _inventoryHoverDialog.Update(deltaTime);
        if (_isExpanded)
        {
            _inventoryHoverDialog.SetVisible(InputManager.Instance.IsMouseOverRectangle(_inventorySpriteBounds));
        }

        _socialSprite.Update(deltaTime);
        _socialHoverDialog.Update(deltaTime);
        if (_isExpanded)
        {
            _socialHoverDialog.SetVisible(InputManager.Instance.IsMouseOverRectangle(_socialSpriteBounds));
        }

        _questsSprite.Update(deltaTime);
        _questsHoverDialog.Update(deltaTime);
        if (_isExpanded)
        {
            _questsHoverDialog.SetVisible(InputManager.Instance.IsMouseOverRectangle(_questsSpriteBounds));
        }
        
        _settingsSprite.Update(deltaTime);
        _settingsHoverDialog.Update(deltaTime);
        if (_isExpanded)
        {
            _settingsHoverDialog.SetVisible(InputManager.Instance.IsMouseOverRectangle(_settingsSpriteBounds));
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (_isExpanded)
        {
            _backgroundSprite.Draw(spriteBatch);
            _characterSprite.Draw(spriteBatch);
            _inventorySprite.Draw(spriteBatch);
            _socialSprite.Draw(spriteBatch);
            _questsSprite.Draw(spriteBatch);
            _settingsSprite.Draw(spriteBatch);

            _characterHoverDialog.Draw(spriteBatch);
            _inventoryHoverDialog.Draw(spriteBatch);
            _socialHoverDialog.Draw(spriteBatch);
            _questsHoverDialog.Draw(spriteBatch);
            _settingsHoverDialog.Draw(spriteBatch);
        }
        else
        {
            _smallBackgroundSprite.Draw(spriteBatch);
        }
        _showHideSprite.Draw(spriteBatch);
        _showHideHoverDialog.Draw(spriteBatch);
        
    }
    
    public void Dispose()
    {
        _smallBackgroundSprite.Dispose();
        _backgroundSprite.Dispose();
        _showHideSprite.Dispose();
        _showHideHoverDialog.Dispose();
        _characterSprite.Dispose();
        _characterHoverDialog.Dispose();
        _inventorySprite.Dispose();
        _inventoryHoverDialog.Dispose();
        _socialSprite.Dispose();
        _socialHoverDialog.Dispose();
        _questsSprite.Dispose();
        _questsHoverDialog.Dispose();
        _settingsSprite.Dispose();
        _settingsHoverDialog.Dispose();
    }

    public void ToggleVisibility()
    {
        _isExpanded = !_isExpanded;
    }
}
