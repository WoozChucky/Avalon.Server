using System.Collections.Generic;

namespace Avalon.Map
{
    public class MapLayer
    {
        private readonly Tile[,] _tiles;
        
        public Tile? this[int x, int y]
        {
            get
            {
                if (x < 0 || x >= _tiles.GetLength(0) || y < 0 || y >= _tiles.GetLength(1))
                {
                    return null;
                }
                return _tiles[x, y];
            }
        }
    }
}