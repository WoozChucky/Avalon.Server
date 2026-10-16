namespace Avalon.Infrastructure.GameAuth;

/// <summary>
/// Told of every game context credential of an account that <see cref="GameAuthorizationService"/> commits (a sign-in, a
/// link, a refresh), so identity's queue limiter knows it on its first check-in (#967), and of the credential it replaces
/// (<c>replacedDigest</c>, its <see cref="GameAuthCryptography.Digest"/>), which is current no more. Never
/// told a credential that was not committed. Must never throw: the commit it follows has happened.
/// </summary>
public interface IGameCredentialIssued
{
    void Issued(string credential, DateTime expiresAtUtc, string? replacedDigest);
}
