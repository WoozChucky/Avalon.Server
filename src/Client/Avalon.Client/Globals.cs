using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client;

public static class Globals
{
    public static float Time { get; set; }
    public static ContentManager Content { get; set; }
    public static GraphicsDevice GraphicsDevice { get; set; }
    
    public static Guid ClientId { get; set; }

    public static Point WindowSize { get; set; }
    public static Vector2 CameraPosition { get; set; }

    public static void Update(GameTime gt)
    {
        Time = (float)gt.ElapsedGameTime.TotalSeconds;
    }
    
}
