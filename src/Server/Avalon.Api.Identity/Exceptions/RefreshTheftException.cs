using Avalon.Common.ValueObjects;

namespace Avalon.Api.Identity.Exceptions;

public sealed class RefreshTheftException : Exception
{
    public AccountId AccountId { get; }

    public RefreshTheftException(AccountId accountId)
        : base($"Refresh token reuse detected for account {accountId.Value}; family revoked.")
    {
        AccountId = accountId;
    }
}
