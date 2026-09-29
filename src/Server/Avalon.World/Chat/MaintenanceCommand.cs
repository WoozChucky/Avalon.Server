using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.WorldMaintenance;
using Avalon.Network.Packets.Social;
using Avalon.World.Maintenance;

namespace Avalon.World.Chat;

public sealed class MaintenanceCommand(
    WorldId worldId,
    IWorldMaintenanceRepository repository,
    IWorldMaintenanceControl control,
    WorldMaintenanceCoordinator coordinator) : ICommand
{
    private const string Usage = "Usage: /maintenance <on [minutes]|off|status> (minutes: 1-60)";

    public string Name => "maintenance";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccountAccessLevel.Admin;

    public async Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args,
        CancellationToken token = default)
    {
        if (args.Length == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            WorldMaintenanceState? persisted = await repository.ReadAsync(worldId, token);
            Reply(ctx, persisted is null ? "World maintenance state is unavailable." : Describe(persisted));
            return;
        }

        bool enabled;
        int minutes = 5;
        if (args.Length is 1 or 2 && string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            if (args.Length == 2 && (!int.TryParse(args[1], out minutes) || minutes is < 1 or > 60))
            {
                Reply(ctx, Usage);
                return;
            }
        }
        else if (args.Length == 1 && string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
        }
        else
        {
            Reply(ctx, Usage);
            return;
        }

        WorldMaintenanceState? committed = await control.SetAsync(worldId, enabled,
            TimeSpan.FromMinutes(minutes), $"account:{ctx.Connection.AccountId?.Value}", token);
        if (committed is null)
        {
            Reply(ctx, "World maintenance state is unavailable.");
            return;
        }

        coordinator.ApplyCommitted(committed);
        Reply(ctx, Describe(committed));
    }

    private static string Describe(WorldMaintenanceState state)
        => $"Maintenance {(state.Enabled ? "enabled" : "disabled")}, revision {state.Revision}, " +
           (state.DeadlineUtc is { } deadline
               ? $"deadline {deadline.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC."
               : "no deadline.");

    private static void Reply(WorldPacketContext<CChatMessagePacket> ctx, string message)
        => ctx.Connection.Send(SChatMessagePacket.Create(0, 0, "System", message, ctx.Packet.DateTime,
            ctx.Connection.CryptoSession.Encrypt));
}
