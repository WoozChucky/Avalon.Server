using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.State;

namespace Avalon.World.Public;

public enum PowerType
{
    None,
    Mana,
    Fury,
    Energy
}

public interface IObject
{
    ObjectGuid Guid { get; init; }
    
    static uint GenerateId()
    {
        return UniqueObjectIdGenerator.GenerateId();
    }
}

internal static class UniqueObjectIdGenerator
{
    private static uint _nextId = 1;
    private static readonly object Lock = new object();

    public static uint GenerateId()
    {
        lock (Lock)
        {
            return _nextId++;
        }
    }
}

public interface IWorldObject : IObject
{
    Vector3 Position { get; set; }
    Vector3 Velocity { get; set; }
    Vector3 Orientation { get; set; }
}

public interface IUnit : IWorldObject
{
    ushort Level { get; set; }
    uint Health { get; set; }
    uint CurrentHealth { get; set; }
    PowerType PowerType { get; set; }
    uint? Power { get; set; }
    uint? CurrentPower { get; set; }
    MoveState MoveState { get; set; }
    
    void OnHit(IUnit attacker, uint damage);
}
