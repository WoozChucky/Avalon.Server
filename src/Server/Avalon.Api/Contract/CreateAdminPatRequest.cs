namespace Avalon.Api.Contract;
public sealed class CreateAdminPatRequest
{
    public long AccountId { get; set; }
    public string Name { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public AccountAccessLevel Roles { get; set; }

    /// <summary>
    /// The calling admin's own current password (#483), as for a self-service token: without it a
    /// stolen admin session could mint a long-lived token for any account, its own included.
    /// </summary>
    public string CurrentPassword { get; set; } = "";
}
