namespace Avalon.Common.ValueObjects;

public class ItemTemplateId : ValueObject<ulong>, IHideObjectMembers
{
    public ItemTemplateId(ulong value) : base(value) {}
    
    public static implicit operator ItemTemplateId(ulong value)
    {
        return new ItemTemplateId(value);
    }
}
