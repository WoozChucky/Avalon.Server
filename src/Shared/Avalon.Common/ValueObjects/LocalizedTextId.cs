namespace Avalon.Common.ValueObjects;

public class LocalizedTextId : ValueObject<int>, IHideObjectMembers
{
    public LocalizedTextId(int value) : base(value) { }

    public static implicit operator LocalizedTextId(int value)
    {
        return new LocalizedTextId(value);
    }
}
