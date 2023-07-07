namespace Avalon.Game.Maps.Virtual;

public class MapEvent : BaseMapElement
{
    public int EventId { get; private set; }
    
    public MapEvent(int id, int x, int y, int width, int height) : base(x, y, width, height)
    {
        EventId = id;
    }
}
