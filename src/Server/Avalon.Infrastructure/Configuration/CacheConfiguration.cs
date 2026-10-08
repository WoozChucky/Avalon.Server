using System.ComponentModel.DataAnnotations;

namespace Avalon.Infrastructure.Configuration;

public class CacheConfiguration
{
    [Required]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// The Redis ACL user to sign in as (#803). Left out, the connection signs in as the default user with
    /// <see cref="Password"/> alone, as before.
    /// </summary>
    public string? Username { get; set; }

    public string? Password { get; set; }
}
