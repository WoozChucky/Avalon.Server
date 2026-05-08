namespace Avalon.Common.ValueObjects;

public class AbilityId : ValueObject<uint>
{
    public AbilityId(uint value) : base(value) { }

    public static implicit operator AbilityId(uint value) => new(value);
    public static implicit operator uint(AbilityId id) => id.Value;
}
