namespace Avalon.Common.ValueObjects;

public class ChunkPoolId : ValueObject<ushort>, IHideObjectMembers
{
    public ChunkPoolId(ushort value) : base(value) { }

    public static implicit operator ChunkPoolId(ushort value)
    {
        return new ChunkPoolId(value);
    }
}
