namespace Avalon.Common.ValueObjects;

public class LootTableId : ValueObject<int>, IHideObjectMembers
{
    public LootTableId(int value) : base(value) { }

    public static implicit operator LootTableId(int value)
    {
        return new LootTableId(value);
    }
}
