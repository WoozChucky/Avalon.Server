using Avalon.Client.Managers;
using Avalon.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.UI;

public class InGameRightPanel : IGameComponent
{

    private volatile bool _isExpanded;
    
    private readonly CameraFollowingSprite _smallBackgroundSprite;
    private readonly CameraFollowingSprite _backgroundSprite;
    
    private readonly CameraFollowingSprite _showHideSprite;
    private readonly Rectangle _showHideSpriteBounds;
    
    private readonly CameraFollowingSprite _characterSprite;
    private readonly CameraFollowingSprite _inventorySprite;
    private readonly CameraFollowingSprite _socialSprite;
    private readonly CameraFollowingSprite _questsSprite;
    private readonly CameraFollowingSprite _settingsSprite;
    
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
        
        _characterSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Character"),
            new Vector2(0, 0)
        );
        _characterSprite.Position = new Vector2(
            Globals.WindowSize.X - _characterSprite.Texture.Width - 20,
            _showHideSprite.Position.Y + _characterSprite.Texture.Height + 10
        );
        
        _inventorySprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Inventory"),
            new Vector2(0, 0)
        );
        _inventorySprite.Position = new Vector2(
            Globals.WindowSize.X - _inventorySprite.Texture.Width - 20,
            _characterSprite.Position.Y + _inventorySprite.Texture.Height + 10
        );
        
        _socialSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Social"),
            new Vector2(0, 0)
        );
        _socialSprite.Position = new Vector2(
            Globals.WindowSize.X - _socialSprite.Texture.Width - 20,
            _inventorySprite.Position.Y + _socialSprite.Texture.Height + 10
        );
        
        _questsSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Quests"),
            new Vector2(0, 0)
        );
        _questsSprite.Position = new Vector2(
            Globals.WindowSize.X - _questsSprite.Texture.Width - 20,
            _socialSprite.Position.Y + _questsSprite.Texture.Height + 10
        );
        
        _settingsSprite = new CameraFollowingSprite(
            Globals.Content.Load<Texture2D>("Images/Icons/Settings"),
            new Vector2(0, 0)
        );
        _settingsSprite.Position = new Vector2(
            Globals.WindowSize.X - _settingsSprite.Texture.Width - 20,
            _questsSprite.Position.Y + _settingsSprite.Texture.Height + 10
        );
        
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
        _characterSprite.Update(deltaTime);
        _inventorySprite.Update(deltaTime);
        _socialSprite.Update(deltaTime);
        _questsSprite.Update(deltaTime);
        _settingsSprite.Update(deltaTime);
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
        }
        else
        {
            _smallBackgroundSprite.Draw(spriteBatch);
        }
        _showHideSprite.Draw(spriteBatch);
        
    }
    
    public void Dispose()
    {
        _smallBackgroundSprite.Dispose();
        _backgroundSprite.Dispose();
        _showHideSprite.Dispose();
        _characterSprite.Dispose();
        _settingsSprite.Dispose();
    }

    public void ToggleVisibility()
    {
        
    }
}
