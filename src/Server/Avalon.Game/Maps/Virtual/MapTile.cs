using System.Drawing;

namespace Avalon.Game.Maps.Virtual
{
    public class MapTile : BaseMapElement
    {
        public bool IsCollidable { get; private set; }
        public int Row { get; private set; }
        public int Col { get; private set; }
        
        public MapTile(int row, int col, int x, int y, int size, bool collidable = false) : base(x, y, size, size)
        {
            Row = row;
            Col = col;
            IsCollidable = collidable;
        }
    }
}
