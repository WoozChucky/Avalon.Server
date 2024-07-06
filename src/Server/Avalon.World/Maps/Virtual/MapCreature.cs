namespace Avalon.World.Maps.Virtual;

public class MapCreature : BaseMapElement
{
    public ulong TemplateId { get; private set; }
    
    public MapCreature(ulong id, int x, int y, int size) : base(x, y, size, size)
    {
        TemplateId = id;
    }
}
