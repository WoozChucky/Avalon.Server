namespace Avalon.Common.ValueObjects;

public class MapTemplateId : ValueObject<ushort>, IHideObjectMembers
{
    public MapTemplateId(ushort value) : base(value) { }

    public static implicit operator MapTemplateId(ushort value)
    {
        return new MapTemplateId(value);
    }
}
