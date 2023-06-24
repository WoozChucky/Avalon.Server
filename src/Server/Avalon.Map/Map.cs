using System;
using System.Collections.Generic;

namespace Avalon.Map
{
    public class Map
    {
        private readonly ICollection<MapLayer> _layers;

        public Map()
        {
            _layers = new List<MapLayer>();
        }


    }
}