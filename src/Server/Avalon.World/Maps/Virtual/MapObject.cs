namespace Avalon.World.Maps.Virtual;

public class MapObject : BaseMapElement
{
    public int ObjectId { get; private set; }
    
    public MapObject(int id, int x, int y, int width, int height) : base(x, y, width, height)
    {
        ObjectId = id;
    }
}
