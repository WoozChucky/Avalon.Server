namespace Avalon.Common.ValueObjects;

public class CharacterId : ValueObject<ulong>, IHideObjectMembers
{
    public CharacterId(ulong value) : base(value)
    {
    }
    
    public static implicit operator CharacterId(ulong value)
    {
        return new CharacterId(value);
    }
}
