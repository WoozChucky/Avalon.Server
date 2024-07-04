using System.ComponentModel.DataAnnotations;

namespace Avalon.Infrastructure.Configuration;

public class CacheConfiguration
{
    [Required]
    public string Host { get; set; } = string.Empty;
    public string? Password { get; set; }
}
