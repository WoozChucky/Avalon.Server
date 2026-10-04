using Avalon.Common.ValueObjects;

namespace Avalon.Common.GameAuth;

/// <summary>A server-owned immutable snapshot of the admitted writer; never taken from a gameplay packet.</summary>
public sealed class GameplayWriteAuthority(AccountId accountId, Guid gameSessionId, long fencingToken)
{
    public AccountId AccountId { get; } = accountId;
    public Guid GameSessionId { get; } = gameSessionId;
    public long FencingToken { get; } = fencingToken;
}
