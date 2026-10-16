namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// Told of every game context credential <see cref="GameAuthorizationService"/> commits (a sign-in, a link, a refresh), so
/// identity's queue limiter knows it on its first check-in (#967). Never told a credential that was not committed.
/// </summary>
public interface IGameCredentialIssued
{
    void Issued(string credential, DateTime expiresAtUtc);
}
