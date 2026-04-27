namespace Avalon.Common.ValueObjects;

public class ChunkTemplateId : ValueObject<int>, IHideObjectMembers
{
    public ChunkTemplateId(int value) : base(value) { }

    public static implicit operator ChunkTemplateId(int value)
    {
        return new ChunkTemplateId(value);
    }
}
