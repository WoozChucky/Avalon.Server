namespace Avalon.Common.ValueObjects;

public class SpellId : ValueObject<uint>
{
    public SpellId(uint value) : base(value) { }

    public static implicit operator SpellId(uint value)
    {
        return new SpellId(value);
    }
}
