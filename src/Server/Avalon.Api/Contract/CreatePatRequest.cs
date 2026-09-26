namespace Avalon.Api.Contract;
public sealed class CreatePatRequest
{
    public string Name { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public AccountAccessLevel? Roles { get; set; }

    /// <summary>
    /// The caller's current password (#483): minting a token that outlives the session needs the
    /// password, not just a session. A wrong one counts as a failed login.
    /// </summary>
    public string CurrentPassword { get; set; } = "";
}
