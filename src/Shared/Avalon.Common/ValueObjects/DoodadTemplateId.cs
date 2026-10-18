namespace Avalon.Common.ValueObjects;

public class DoodadTemplateId : ValueObject<uint>
{
    public DoodadTemplateId(uint value) : base(value) { }

    public static implicit operator DoodadTemplateId(uint value) => new(value);
    public static implicit operator uint(DoodadTemplateId id) => id.Value;
}
