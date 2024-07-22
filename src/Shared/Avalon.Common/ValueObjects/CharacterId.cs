namespace Avalon.Common.ValueObjects;

public class CharacterId : ValueObject<uint>, IHideObjectMembers
{
    public CharacterId(uint value) : base(value)
    {
    }
    
    public static implicit operator uint(CharacterId characterId) => characterId.Value;
    public static implicit operator CharacterId(uint value) => new CharacterId(value);
}
