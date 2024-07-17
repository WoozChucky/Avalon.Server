using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;

namespace Avalon.World.Public;

public interface IGameEntity
{
    public ulong Id { get; set; }
    public Vector3 Position { get; set; }
    public Vector3 Orientation { get; set; }
    public Vector3 Velocity { get; set; }
    public ushort Level { get; set; }
    public uint Health { get; set; }
    public uint CurrentHealth { get; set; }
    public uint Power { get; set; }
    public uint CurrentPower { get; set; }
    public MoveState MoveState { get; set; }
    
    
    static ulong GenerateId()
    {
        return GameEntityIdGenerator.GenerateId();
    }
}


internal static class GameEntityIdGenerator
{
    private static ulong _nextId = 1;
    private static readonly object _lock = new object();

    public static ulong GenerateId()
    {
        lock (_lock)
        {
            return _nextId++;
        }
    }
}
