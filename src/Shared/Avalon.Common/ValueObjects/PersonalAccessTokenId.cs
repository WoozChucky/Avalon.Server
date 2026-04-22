namespace Avalon.Common.ValueObjects;

public class PersonalAccessTokenId : ValueObject<uint>, IHideObjectMembers
{
    public PersonalAccessTokenId(uint value) : base(value)
    {
    }

    public static implicit operator uint(PersonalAccessTokenId personalAccessTokenId) => personalAccessTokenId.Value;
    public static implicit operator PersonalAccessTokenId(uint value) => new PersonalAccessTokenId(value);
}
