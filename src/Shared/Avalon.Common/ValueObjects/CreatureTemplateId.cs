namespace Avalon.Common.ValueObjects;

public class CreatureTemplateId : ValueObject<ulong>, IHideObjectMembers
{
    public CreatureTemplateId(ulong value) : base(value) {}
    
    public static implicit operator CreatureTemplateId(ulong value)
    {
        return new CreatureTemplateId(value);
    }
}
