using System.Drawing;
using System.Numerics;
using Avalon.Common;

namespace Avalon.Game.Maps.Virtual;

public interface IMapElement : IHideObjectMembers
{
    Vector2 Position { get; }
    Vector2 Origin { get; }
    Rectangle Bounds { get; }
    Size Size { get; }
}
