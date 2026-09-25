namespace Avalon.Common.ValueObjects;

public class CreaturePathId : ValueObject<int>, IHideObjectMembers
{
    public CreaturePathId(int value) : base(value) { }

    public static implicit operator CreaturePathId(int value)
    {
        return new CreaturePathId(value);
    }
}
