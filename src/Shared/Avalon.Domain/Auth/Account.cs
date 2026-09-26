using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

public class Account : IDbEntity<AccountId>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public AccountId Id { get; set; }

    [Required]
    public required string Username { get; init; }

    [Required]
    public required byte[] Salt { get; set; }

    [Required]
    public required byte[] Verifier { get; set; }

    [Column("SessionKey")]
    public byte[] SessionKey { get; set; } = [];

    [Required]
    public required string Email { get; set; }

    [Required]
    public required DateTime JoinDate { get; init; } = DateTime.UtcNow;

    public string LastIp { get; set; } = string.Empty;

    public string LastAttemptIp { get; set; } = string.Empty;

    public int FailedLogins { get; set; }

    public bool Locked { get; set; } = false;

    /// <summary>
    /// When a lock set by failed logins ends (#471). <c>null</c> on a locked account means the
    /// lock has no end and stays until cleared.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>Locked at <paramref name="utcNow"/>: a lock with no end, or one that has not ended yet.</summary>
    public bool IsLockedAt(DateTime utcNow) => Locked && (LockedUntil is null || LockedUntil > utcNow);

    public DateTime LastLogin { get; set; }

    public bool Online { get; set; } = false;

    /// <summary>
    /// The auth-server connection whose login set <see cref="Online"/> (#487), or <c>null</c> while
    /// offline. A connection clears the flag on close only while this is still its own id, so a
    /// stale disconnect cannot mark offline an account a newer session is using.
    /// </summary>
    public Guid? OnlineSessionId { get; set; }

    public DateTime? MuteTime { get; set; }

    public string MuteReason { get; set; } = string.Empty;

    public string MuteBy { get; set; } = string.Empty;

    public AccountLocale Locale { get; set; } = AccountLocale.enUS;

    public OperatingSystem Os { get; set; } = OperatingSystem.Windows;

    public long TotalTime { get; set; } = 0;

    public AccountAccessLevel AccessLevel { get; set; } = AccountAccessLevel.Player;

    public AccountStatus Status { get; set; } = AccountStatus.Active;

    /// <summary>
    /// A counter raised by one, in the transaction that makes the change, by every password change,
    /// owner MFA reset and admin MFA removal (#495). Everything issued on the strength of the
    /// credentials (an access token's <c>cver</c> claim, a refresh token, an MFA hash, a
    /// re-authentication, a TCP login and its world key) carries the value read from the same row
    /// that proved them, and is refused once it no longer equals this.
    /// </summary>
    public int CredentialsVersion { get; set; }
}

public enum OperatingSystem : ushort
{
    Windows,
    MacOS,
    Linux
}

public enum AccountStatus : byte
{
    Active = 0,
    Banned = 1,
    Deactivated = 2
}
