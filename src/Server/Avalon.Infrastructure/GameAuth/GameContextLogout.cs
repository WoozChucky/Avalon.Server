namespace Avalon.Infrastructure.GameAuth;

/// <summary>What a game-context logout did (<see cref="GameAuthorizationService.LogoutAsync"/>).</summary>
public enum GameContextLogout
{
    /// <summary>The context is revoked: by this logout, or already by another writer.</summary>
    Ended,

    /// <summary>The credential is not a current one (unknown, rotated, spent or expired): nothing was ended.</summary>
    Unknown,

    /// <summary>The context kept changing under every retry and is still live: the caller should log out again.</summary>
    Contended,
}
