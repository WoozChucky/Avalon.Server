using Avalon.Common.Mathematics;

namespace Avalon.World.Public;

public interface IGameEntity<TKey>
{
    public TKey Id { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Orientation { get; set; }
    public Vector3 Velocity { get; set; }
}
