using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class WorldMaintenanceRequest
{
    [Range(1, 60)]
    public int GraceMinutes { get; set; } = 5;
}
