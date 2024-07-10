using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

public class ItemTemplate : IDbEntity<ItemTemplateId>
{
    public ItemTemplateId Id { get; set; }
}
