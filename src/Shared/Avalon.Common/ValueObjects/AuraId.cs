namespace Avalon.Common.ValueObjects;

public class AuraId : ValueObject<uint>
{
    public AuraId(uint value) : base(value) { }

    public static implicit operator AuraId(uint value) => new(value);
    public static implicit operator uint(AuraId id) => id.Value;
}
