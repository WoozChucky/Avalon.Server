namespace Avalon.Api.Contract;

/// <summary>What the signed-in website asks a launcher sign-in code for (#591).</summary>
public sealed class ClientAuthCodeRequest
{
    /// <summary>PKCE S256 challenge: base64url, no padding, of SHA-256 over the launcher's verifier.</summary>
    public string Challenge { get; set; } = string.Empty;

    /// <summary>The loopback port the launcher listens on; the website redirects to 127.0.0.1 on it.</summary>
    public int RedirectPort { get; set; }

    /// <summary>
    /// The account's current password: a launcher session outlives the website's, so, as for a personal
    /// access token (#483), the session alone is not enough to open one (#591 review).
    /// </summary>
    public string CurrentPassword { get; set; } = string.Empty;
}

public sealed class ClientAuthCodeResponse
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>What the launcher trades for its tokens (#591).</summary>
public sealed class ClientAuthTokenRequest
{
    public string Code { get; set; } = string.Empty;
    public string Verifier { get; set; } = string.Empty;

    /// <summary>The loopback port the code was issued for (RFC 6749 §4.1.3): another one voids the exchange.</summary>
    public int RedirectPort { get; set; }

    /// <summary>What to call this computer in the account's list of launcher sessions.</summary>
    public string? DeviceName { get; set; }
}

public sealed class ClientAuthRefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>One of the account's signed-in launchers (#591).</summary>
public sealed class LauncherSessionDto
{
    public Guid Id { get; set; }

    /// <summary>What the launcher called its computer; null when it gave no name.</summary>
    public string? DeviceName { get; set; }

    public DateTime SignedInAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>A launcher's tokens, in the body: its session is never a cookie (#591).</summary>
public sealed class ClientAuthTokens
{
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Unix seconds.</summary>
    public long ExpiresAt { get; set; }

    /// <summary>Rotated by every refresh; presenting a rotated one again ends the session.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Unix seconds.</summary>
    public long RefreshExpiresAt { get; set; }
}
