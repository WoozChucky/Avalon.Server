namespace Avalon.Common.ValueObjects;

public class MapChunkPlacementId : ValueObject<int>, IHideObjectMembers
{
    public MapChunkPlacementId(int value) : base(value) { }

    public static implicit operator MapChunkPlacementId(int value)
    {
        return new MapChunkPlacementId(value);
    }
}
