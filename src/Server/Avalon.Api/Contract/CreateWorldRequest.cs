using System.ComponentModel.DataAnnotations;
using Avalon.Domain.Auth;

namespace Avalon.Api.Contract;

public sealed class CreateWorldRequest
{
    [Required] public string Name { get; set; } = "";
    [Required] public string Host { get; set; } = "";
    [Required] public int Port { get; set; }
    [Required] public string MinVersion { get; set; } = "";
    [Required] public string Version { get; set; } = "";
    public WorldType Type { get; set; } = WorldType.PvE;
    public AccountAccessLevel AccessLevelRequired { get; set; } = AccountAccessLevel.Player;
    public WorldStatus Status { get; set; } = WorldStatus.Offline;
}
