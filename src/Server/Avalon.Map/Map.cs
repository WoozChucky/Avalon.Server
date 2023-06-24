using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalon.Map
{
    public class Map
    {
        private readonly ICollection<MapLayer> _layers;

        public Map()
        {
            _layers = new List<MapLayer>();
        }

        public Tile? this[int layer, int x, int y]
        {
            get
            {
                return _layers.ElementAt(layer)[x, y];
            }
        }
        
        public MapLayer? this[int index]
        {
            get
            {
                if (index < 0 || index >= _layers.Count)
                {
                    return null;
                }
                return _layers.ElementAt(index);
            }
        }
    }
}