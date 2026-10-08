namespace Avalon.Api.Identity.Config;

/// <summary>
/// <c>Application:GameAuth</c>, read by identity (#801, design D4.3): the key its game-auth cryptography derives the
/// proof, replay receipt and Steam OpenID state keys from (<see cref="Authentication.GameAuthHostKey"/>).
/// </summary>
public sealed class GameAuthConfig
{
    public const string HostKeySetting = "Application:GameAuth:HostKey";

    /// <summary>
    /// A random secret of at least 32 bytes, never committed, required where identity runs. A deployment from before #801
    /// gives it the value its HS256 signing key (<c>Application:Authentication:IssuerSigningKey</c>, ignored since) held,
    /// from which these keys were derived then, so what was protected with them stays readable.
    /// </summary>
    public string HostKey { get; set; } = string.Empty;
}
