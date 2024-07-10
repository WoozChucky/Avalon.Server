namespace Avalon.Common.ValueObjects;

public class AccountId : ValueObject<ulong>, IHideObjectMembers
{
    public AccountId(ulong value) : base(value)
    {
    }
    
    public static implicit operator ulong(AccountId accountId) => accountId.Value;
    public static implicit operator AccountId(ulong value) => new AccountId(value);
}
