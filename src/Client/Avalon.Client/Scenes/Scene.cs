using System;
using Avalon.Client.Managers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Scenes;

public abstract class Scene : IDisposable
{
    protected SceneManager SceneManager { get; }

    protected Scene(SceneManager sceneManager)
    {
        SceneManager = sceneManager;
    }

    public virtual void Load()
    {
        // TODO: Load any necessary content for the scene
    }

    public virtual void Unload()
    {
        // TODO: Unload any content that was loaded for the scene
    }

    public virtual void Update(GameTime gameTime)
    {
        // TODO: Update the scene's logic
    }

    public virtual void Draw(SpriteBatch spriteBatch)
    {
        // TODO: Draw the scene's graphics
    }

    protected abstract void Dispose(bool disposing);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
