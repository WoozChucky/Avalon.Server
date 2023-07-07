namespace Avalon.Game.Maps.Virtual;

public class MapCreaturePool : BaseMapElement
{
    public int PoolId { get; private set; }
    
    public MapCreaturePool(int id, int x, int y, int width, int height) : base(x, y, width, height)
    {
        PoolId = id;
    }
}
