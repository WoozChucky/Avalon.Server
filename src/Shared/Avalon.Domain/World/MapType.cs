namespace Avalon.Domain.World;

public enum MapType
{
    Town   = 0,   // Shared hub; multiple instances with MaxPlayers cap; never freed
    Normal = 1,   // Private instanced map; 15-minute expiry timer after last player leaves
}
