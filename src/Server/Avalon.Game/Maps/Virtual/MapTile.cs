namespace Avalon.Game.Maps.Virtual
{
    public class MapTile : BaseMapElement
    {
        public bool IsCollidable { get; private set; }
        
        public MapTile(int x, int y, int size, bool collidable = false) : base(x, y, size, size)
        {
            IsCollidable = collidable;
        }
    }
}
