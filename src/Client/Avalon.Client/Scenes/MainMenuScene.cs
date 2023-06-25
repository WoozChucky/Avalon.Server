using Avalon.Client.Managers;
using Avalon.Client.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Scenes;

public class MainMenuScene : Scene
{
    private SpriteFont _font;
    private Texture2D _label;
    private Banner _banner;

    public MainMenuScene(SceneManager sceneManager) : base(sceneManager)
    {
    }

    public override void Load()
    {
        _banner = new Banner(new Vector2(Globals.WindowSize.X / 2f, Globals.WindowSize.Y / 2f), "Nuno", 2f, 0.9f);
        _banner.Load();
    }

    public override void Unload()
    {
        // TODO: Unload any content that was loaded for the scene
        _banner.Unload();
    }

    public override void Update(GameTime gameTime)
    {
        _banner.Update(gameTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(sortMode: SpriteSortMode.FrontToBack, blendState: BlendState.AlphaBlend);
        
        _banner.Draw(spriteBatch);
        
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _label?.Dispose();
        }
    }
}
