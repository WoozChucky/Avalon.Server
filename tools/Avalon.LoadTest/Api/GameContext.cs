namespace Avalon.LoadTest.Api;

/// <summary>
/// A bot's signed-in game context: the credential join tickets are asked with, and the refresh token that rotates it.
/// The credential lives 5 minutes and the license authorization as long; a world session's lease ends with the
/// authorization, so a bot refreshes at <see cref="RenewAt"/>, a minute before the earlier of the two.
/// </summary>
public sealed class GameContext
{
    /// <summary>The game context credential (<c>gameContextCredential</c>).</summary>
    public required string Credential { get; set; }

    /// <summary>The one-use refresh token (<c>gameContextRefreshToken</c>); each refresh replaces it.</summary>
    public required string RefreshToken { get; set; }

    /// <summary>When the credential expires.</summary>
    public DateTimeOffset ContextExpiresAt { get; set; }

    /// <summary>When the license authorization, and with it the world session's lease, runs out.</summary>
    public DateTimeOffset AuthorizationValidUntil { get; set; }

    /// <summary>When to refresh: a minute before the credential or the authorization expires, whichever is first.</summary>
    public DateTimeOffset RenewAt =>
        (ContextExpiresAt < AuthorizationValidUntil ? ContextExpiresAt : AuthorizationValidUntil) - TimeSpan.FromSeconds(60);
}

/// <summary>The world server a join ticket is for, as the join reply names it.</summary>
/// <param name="TlsServerName">The name the world's certificate carries, sent as SNI.</param>
/// <param name="TlsCertificateSha256">The SHA-256 of the world's complete DER leaf certificate, the pin.</param>
public sealed record WorldDestination(ushort WorldId, string Host, int Port, string TlsServerName, string TlsCertificateSha256);

/// <summary>A one-use join ticket (30 seconds at most) and the world server that redeems it.</summary>
public sealed record JoinTicket(string Ticket, WorldDestination Destination);
