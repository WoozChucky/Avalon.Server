using System;
using Avalon.Client.Managers;
using Avalon.Client.Maps;
using Avalon.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client.Scenes;

public class TutorialScene : Scene
{
    private Map _map;
    private Hero _hero;
    private Matrix _translation;
    private SpriteFont _font;
    
    private float _elapsedTime;
    private int _frameCount;
    private int _fps;
    
    private SceneManager _sceneManager;

    public TutorialScene(SceneManager sceneManager) : base(sceneManager)
    {
        Globals.CameraPosition = Vector2.Zero;
    }

    public override void Load()
    {
        _font = Globals.Content.Load<SpriteFont>("Fonts/Nintendo");
        _map = new Map("Tutorial", "Serene_Village_32x32");
        _hero = new Hero(
            Globals.Content.Load<Texture2D>("Images/player"), 
            //new Vector2(Globals.WindowSize.X / 2f, Globals.WindowSize.Y / 2f),
            new Vector2(326, 1450)
        );
        _hero.SetBounds(_map.MapSize, new Point(_map.TileWidth, _map.TileHeight));
    }

    public override void Unload()
    {
        // TODO: Unload any content that was loaded for the scene
    }

    public override void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _elapsedTime += deltaTime;
        _frameCount++;
        
        // TODO: Update the scene's logic
        InputManager.Update();
        _hero.Update(_map.IsObjectColliding);
        

        // Update the camera position based on player movement or other logic
        Globals.CameraPosition = _hero.Position - new Vector2(Globals.GraphicsDevice.Viewport.Width / 2,
            Globals.GraphicsDevice.Viewport.Height / 2) + new Vector2(_map.TileWidth / 2f, _map.TileHeight / 2f);
        
        // Perform boundary checks to prevent the camera from going outside the game world
        Globals.CameraPosition = new Vector2(
            MathHelper.Clamp(Globals.CameraPosition.X, 0, _map.MapSize.X - Globals.GraphicsDevice.Viewport.Width),
            MathHelper.Clamp(Globals.CameraPosition.Y, 0, _map.MapSize.Y - Globals.GraphicsDevice.Viewport.Height)
        );
        
        CalculateTranslation();
        
        if (_elapsedTime >= 1f) // Calculate FPS every second
        {
            _fps = _frameCount;
            _frameCount = 0;
            _elapsedTime = 0;
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        spriteBatch.Begin(transformMatrix: _translation);
        _map.Draw(spriteBatch);
        _hero.Draw(spriteBatch);
        spriteBatch.DrawString(_font, $"X: {Math.Round(_hero.Position.X, 2)} Y: {Math.Round(_hero.Position.Y, 2)}", new Vector2(10, 10) + Globals.CameraPosition, Color.DarkBlue);
        spriteBatch.DrawString(_font, $"FPS: {_fps}", new Vector2(10, 36) + Globals.CameraPosition, Color.DarkBlue);
        spriteBatch.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _map.Dispose();
            _hero.Dispose();
        }
    }

    private void CalculateTranslation()
    {
        var dx = (Globals.WindowSize.X / 2f) - _hero.Position.X;
        dx = MathHelper.Clamp(dx, -_map.MapSize.X + Globals.WindowSize.X + (_map.TileWidth / 2), _map.TileWidth / 2f);
        var dy = (Globals.WindowSize.Y / 2f) - _hero.Position.Y;
        dy = MathHelper.Clamp(dy, -_map.MapSize.Y + Globals.WindowSize.Y + (_map.TileHeight / 2), _map.TileHeight / 2f);
        _translation = Matrix.CreateTranslation(dx, dy, 0f);
    }
}
