namespace Avalon.Common.ValueObjects;

public class MapCreatureSpawnId : ValueObject<int>, IHideObjectMembers
{
    public MapCreatureSpawnId(int value) : base(value) { }

    public static implicit operator MapCreatureSpawnId(int value)
    {
        return new MapCreatureSpawnId(value);
    }
}
