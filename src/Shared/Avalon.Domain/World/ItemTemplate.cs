using Avalon.Common;

namespace Avalon.Domain.World;

public class ItemTemplate : IDbEntity<ItemTemplateId>
{
    public ItemTemplateId Id { get; set; }
}

public class ItemTemplateId : ValueObject<ulong>
{
    public ItemTemplateId(ulong value) : base(value) {}
    
    public static implicit operator ItemTemplateId(ulong value)
    {
        return new ItemTemplateId(value);
    }
}
