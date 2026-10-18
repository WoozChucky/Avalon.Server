namespace Avalon.Common.ValueObjects;

public class MapDoodadSpawnId : ValueObject<int>, IHideObjectMembers
{
    public MapDoodadSpawnId(int value) : base(value) { }

    public static implicit operator MapDoodadSpawnId(int value)
    {
        return new MapDoodadSpawnId(value);
    }
}
