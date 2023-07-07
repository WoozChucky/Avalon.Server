namespace Avalon.Game.Maps.Virtual
{
    public class TileLayer
    {
        private readonly MapTile[,] _tiles;
        
        public TileLayer(MapTile[,] tiles, bool collidable = false)
        {
            _tiles = tiles;
            IsCollidable = collidable;
        }
        
        public bool IsCollidable { get; private set; }
        
        public MapTile? this[int x, int y]
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
