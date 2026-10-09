using System.Globalization;
using System.Text;

namespace Avalon.LoadTest.Api;

/// <summary>
/// A bot's signed-in game context: the credential join tickets are asked with, and the refresh token that rotates it.
/// The credential lives 5 minutes and the license authorization as long; a world session's lease ends with the
/// authorization, so a bot refreshes at <see cref="RenewAt"/>, a minute before the earlier of the two. A refresh
/// replaces every value at once (<see cref="Current"/> is swapped whole), so a reader on another thread never sees a
/// new credential beside an old expiry.
/// </summary>
public sealed class GameContext(string credential, string refreshToken, DateTimeOffset contextExpiresAt,
    DateTimeOffset authorizationValidUntil)
{
    private GameContextTokens _current =
        new(credential, refreshToken, contextExpiresAt, authorizationValidUntil, Guid.NewGuid());

    /// <summary>Everything the context holds now, read together.</summary>
    public GameContextTokens Current => Volatile.Read(ref _current);

    /// <summary>The game context credential (<c>gameContextCredential</c>).</summary>
    public string Credential => Current.Credential;

    /// <summary>When to refresh: a minute before the credential or the authorization expires, whichever is first.</summary>
    public DateTimeOffset RenewAt => Current.RenewAt;

    /// <summary>
    /// Replaces <paramref name="spent"/> with what its refresh answered, and a fresh refresh key with it; false (nothing
    /// changed) when the context moved on since <paramref name="spent"/> was read.
    /// </summary>
    public bool Rotate(GameContextTokens spent, string credential, string refreshToken, DateTimeOffset contextExpiresAt,
        DateTimeOffset authorizationValidUntil)
    {
        var next = new GameContextTokens(credential, refreshToken, contextExpiresAt, authorizationValidUntil, Guid.NewGuid());
        return Interlocked.CompareExchange(ref _current, next, spent) == spent;
    }

    /// <summary>The context's text without its credentials.</summary>
    public override string ToString() =>
        $"GameContext (renew at {RenewAt.ToString("O", CultureInfo.InvariantCulture)}, credentials redacted)";
}

/// <summary>
/// One generation of a game context. <see cref="RefreshKey"/> is the <c>Idempotency-Key</c> every refresh of this
/// generation's one-use <see cref="RefreshToken"/> is sent with, retries and later passes alike: a refresh whose reply
/// was lost is then answered with what it did, where a new key would spend the token twice and get the context revoked.
/// </summary>
/// <param name="ContextExpiresAt">When the credential expires.</param>
/// <param name="AuthorizationValidUntil">When the license authorization, and with it the world session's lease, runs out.</param>
public sealed record GameContextTokens(string Credential, string RefreshToken, DateTimeOffset ContextExpiresAt,
    DateTimeOffset AuthorizationValidUntil, Guid RefreshKey)
{
    /// <summary>A minute before the credential or the authorization expires, whichever is first.</summary>
    public DateTimeOffset RenewAt =>
        (ContextExpiresAt < AuthorizationValidUntil ? ContextExpiresAt : AuthorizationValidUntil) - TimeSpan.FromSeconds(60);

    /// <summary>The record's text without the credential and the refresh token.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ContextExpiresAt = ").Append(ContextExpiresAt.ToString("O", CultureInfo.InvariantCulture))
            .Append(", AuthorizationValidUntil = ").Append(AuthorizationValidUntil.ToString("O", CultureInfo.InvariantCulture))
            .Append(", credentials redacted");
        return true;
    }
}

/// <summary>The world server a join ticket is for, as the join reply names it.</summary>
/// <param name="TlsServerName">The name the world's certificate carries, sent as SNI.</param>
/// <param name="TlsCertificateSha256">The SHA-256 of the world's complete DER leaf certificate, the pin.</param>
public sealed record WorldDestination(ushort WorldId, string Host, int Port, string TlsServerName, string TlsCertificateSha256);

/// <summary>A one-use join ticket (30 seconds at most) and the world server that redeems it.</summary>
public sealed record JoinTicket(string Ticket, WorldDestination Destination)
{
    /// <summary>The record's text without the ticket, which is a credential until redeemed.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Ticket = (redacted), Destination = ").Append(Destination);
        return true;
    }
}
