using System.Collections.Generic;
using Avalon.Client.Scenes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Managers;

public class SceneManager
{
    private readonly IDictionary<string, Scene> _scenes;
    
    private volatile Scene _currentScene;
    
    private bool _isTransitioning;
    private float _transitionTimer;
    private float _transitionDuration = 0.5f; // seconds
    private Color _transitionColor = Color.Black;
    private Scene _nextScene;
    private Texture2D _blankTexture;

    public SceneManager()
    {
        _scenes = new Dictionary<string, Scene>();
        
    }
    
    public void Initialize()
    {
        _blankTexture = new Texture2D(Globals.GraphicsDevice, 1, 1);
        _blankTexture.SetData(new[] { Color.White });
    }

    public void AddScene(string name, Scene scene)
    {
        _scenes[name] = scene;
    }

    public void LoadScene(string name)
    {
        if (_scenes.TryGetValue(name, out var scene))
        {
            if (_currentScene != null)
            {
                // Start transition
                _isTransitioning = true;
                _transitionTimer = 0f;
                _nextScene = scene;
            }
            else
            {
                _currentScene = scene;
                _currentScene.Load();
            }
        }
    }

    public void Update(GameTime gameTime)
    {
        if (_isTransitioning)
        {
            // Update the transition timer
            _transitionTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (_transitionTimer >= _transitionDuration)
            {
                // Transition complete, load the next scene
                _currentScene?.Unload();
                _currentScene = _nextScene;
                _currentScene.Load();
                _isTransitioning = false;
            }
        }
        else
        {
            _currentScene?.Update(gameTime);
        }
    }

    public void Draw(SpriteBatch spriteBatch)
    {
        if (_isTransitioning)
        {
            // Calculate the current alpha value for the transition based on the timer
            float alpha = MathHelper.Clamp(_transitionTimer / _transitionDuration, 0f, 1f);
            Color transitionColor = new Color(_transitionColor, alpha);

            // Draw the transition color to cover the screen
            spriteBatch.Begin();
            spriteBatch.Draw(_blankTexture, Globals.GraphicsDevice.Viewport.Bounds, transitionColor);
            spriteBatch.End();
        }
        else
        {
            _currentScene?.Draw(spriteBatch);
        }
    }

    public void UnloadContent()
    {
        foreach (var scene in _scenes.Values)
        {
            scene.Unload();
            scene.Dispose();
        }
    }
}
