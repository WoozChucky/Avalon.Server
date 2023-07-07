namespace Avalon.Game.Maps.Virtual;

public class MapCreature : BaseMapElement
{
    public int TemplateId { get; private set; }
    
    public MapCreature(int id, int x, int y, int size) : base(x, y, size, size)
    {
        TemplateId = id;
    }
}
