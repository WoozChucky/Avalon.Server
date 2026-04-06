using System.ComponentModel.DataAnnotations;

namespace Avalon.Server.Auth.Configuration;

public class AuthConfiguration
{
    [Required]
    [RegularExpression(@"^\d+\.\d+\.\d+$", ErrorMessage = "MinClientVersion must be a valid SemVer string (e.g. \"1.2.3\").")]
    public string MinClientVersion { get; set; } = "0.0.1";

    [Required]
    [RegularExpression(@"^\d+\.\d+\.\d+$", ErrorMessage = "ServerVersion must be a valid SemVer string (e.g. \"1.2.3\").")]
    public string ServerVersion { get; set; } = "1.0.0";

    [Range(1, int.MaxValue, ErrorMessage = "MaxFailedLoginAttempts must be at least 1.")]
    public int MaxFailedLoginAttempts { get; set; } = 5;
}
