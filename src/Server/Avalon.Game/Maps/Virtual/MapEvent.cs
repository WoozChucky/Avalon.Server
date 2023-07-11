using System.Drawing;

namespace Avalon.Game.Maps.Virtual;

public class MapEvent : BaseMapElement
{
    public int EventId { get; private set; }
    public string Name { get; private set; }
    public string Class { get; private set; }
    
    public MapEvent(int id, string name, string @class, int x, int y, int width, int height) : base(x, y, width, height)
    {
        EventId = id;
        Name = name;
        Class = @class;
    }
}
