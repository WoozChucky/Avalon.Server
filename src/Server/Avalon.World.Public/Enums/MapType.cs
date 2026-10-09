namespace Avalon.World.Public.Enums;

public enum MapType
{
    Town = 0,   // Shared hub; multiple instances with MaxPlayers cap
    Normal = 1,   // Private instanced map; expires once abandoned (Game:AbandonedInstanceLifetimeMinutes)
}
