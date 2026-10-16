using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class UpdateWorldRequest
{
    public string? Name { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? MinVersion { get; set; }
    public string? Version { get; set; }
    /// <summary>At most 280 characters; null leaves the lore as it is, blank clears it.</summary>
    [StringLength(280)] public string? Lore { get; set; }
    public WorldType? Type { get; set; }
    public AccountAccessLevel? AccessLevelRequired { get; set; }
}
