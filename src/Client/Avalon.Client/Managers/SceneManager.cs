using System.Collections.Generic;
using Avalon.Client.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Managers;

public class SceneManager
{
    private readonly IDictionary<string, Scene> _scenes;
    
    private Scene _currentScene;

    public SceneManager()
    {
        _scenes = new Dictionary<string, Scene>();
    }

    public void AddScene(string name, Scene scene)
    {
        _scenes[name] = scene;
    }

    public void LoadScene(string name)
    {
        if (_scenes.TryGetValue(name, out var scene))
        {
            _currentScene?.Unload();

            _currentScene = scene;
            _currentScene.Load();
        }
    }

    public void Update(GameTime gameTime)
    {
        _currentScene?.Update(gameTime);
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        _currentScene?.Draw(spriteBatch);
    }

    public void UnloadContent()
    {
        _currentScene?.Dispose();
    }
}
