namespace Avalon.Common.ValueObjects;

public class AccountId : ValueObject<long>, IHideObjectMembers
{
    public AccountId(long value) : base(value)
    {
    }

    public static implicit operator long(AccountId accountId) => accountId.Value;
    public static implicit operator AccountId(long value) => new(value);
    public static implicit operator AccountId(string value) => new(long.Parse(value));
}
