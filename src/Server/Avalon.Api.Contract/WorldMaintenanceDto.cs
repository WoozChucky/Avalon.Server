using Avalon.Domain.Auth;

namespace Avalon.Api.Contract;

public sealed class WorldMaintenanceDto
{
    public bool Enabled { get; init; }
    public long Revision { get; init; }
    public DateTime? DeadlineUtc { get; init; }
    public bool Ready { get; init; }

    public static WorldMaintenanceDto From(WorldMaintenanceState state, bool ready) => new()
    {
        Enabled = state.Enabled,
        Revision = state.Revision,
        DeadlineUtc = state.DeadlineUtc,
        Ready = ready,
    };
}
