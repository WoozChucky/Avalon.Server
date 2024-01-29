using System;
using Avalon.Network.Packets.Character;
using Avalon.Network.Tcp;
using Avalon.Network.Udp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Avalon.Client;

public static class Globals
{
    public static float Time { get; set; }
    public static ContentManager Content { get; set; }
    public static GraphicsDevice GraphicsDevice { get; set; }
    
    // Network
    public static AvalonTcpClient Tcp { get; set; }
    //public static AvalonUdpClient Udp { get; set; }
    
    public static int AccountId { get; set; }
    public static int CharacterId { get; set; }
    public static string CharacterName { get; set; }
    public static float CharacterRadius { get; set; }

    public static Point WindowSize { get; set; }
    public static Vector2 CameraPosition { get; set; }
    public static Matrix CameraViewMatrix { get; set; }
    
    public static Vector2 StartPosition { get; set; }
    public static MapInfo MapInfo { get; set; }

    public static void Update(GameTime gt)
    {
        Time = (float)gt.ElapsedGameTime.TotalSeconds;
    }
    
}
