namespace Avalon.Api.Contract;

public sealed class UpdateWorldRequest
{
    public string? Name { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public string? MinVersion { get; set; }
    public string? Version { get; set; }
    public WorldType? Type { get; set; }
    public AccountAccessLevel? AccessLevelRequired { get; set; }
    public WorldStatus? Status { get; set; }
}
