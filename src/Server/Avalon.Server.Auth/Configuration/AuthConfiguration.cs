using System.ComponentModel.DataAnnotations;
using Avalon.Infrastructure.Login;

namespace Avalon.Server.Auth.Configuration;

public class AuthConfiguration : ILoginLimits
{
    [Required]
    [RegularExpression(@"^\d+\.\d+\.\d+$", ErrorMessage = "MinClientVersion must be a valid SemVer string (e.g. \"1.2.3\").")]
    public string MinClientVersion { get; set; } = "0.0.1";

    [Required]
    [RegularExpression(@"^\d+\.\d+\.\d+$", ErrorMessage = "ServerVersion must be a valid SemVer string (e.g. \"1.2.3\").")]
    public string ServerVersion { get; set; } = "1.0.0";

    [Range(1, int.MaxValue, ErrorMessage = "MaxFailedLoginAttempts must be at least 1.")]
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>How long an account stays locked after reaching <see cref="MaxFailedLoginAttempts"/>.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "LockoutDurationMinutes must be at least 1.")]
    public int LockoutDurationMinutes { get; set; } = 15;

    /// <summary>
    /// Failed logins one remote address may make, across all accounts, within
    /// <see cref="FailedLoginSourceWindowMinutes"/>. Past it, that address is refused until the window ends.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MaxFailedLoginsPerSource must be at least 1.")]
    public int MaxFailedLoginsPerSource { get; set; } = 10;

    /// <summary>The window, fixed from a source's first failed login, over which its failures are counted.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "FailedLoginSourceWindowMinutes must be at least 1.")]
    public int FailedLoginSourceWindowMinutes { get; set; } = 15;

    /// <summary>
    /// Code attempts one MFA hash allows. The hash is deleted after this many failures, so the
    /// client must log in with the password again to get another.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MaxFailedMfaAttempts must be at least 1.")]
    public int MaxFailedMfaAttempts { get; set; } = 5;

    [Required]
    public string Issuer { get; set; } = "Avalon";
}
