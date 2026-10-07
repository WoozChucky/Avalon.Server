using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.WorldMaintenance;
using Avalon.World.Maintenance;

namespace Avalon.World.Chat;

/// <summary>
/// /maintenance on [minutes] | off | status. Runs on the tick and never awaits: the database read and the
/// transition run off the tick, and their result is answered, and a committed one applied, on a later tick.
/// </summary>
public sealed class MaintenanceCommand(
    WorldId worldId,
    IWorldMaintenanceRepository repository,
    IWorldMaintenanceControl control,
    WorldMaintenanceCoordinator coordinator) : ICommand
{
    private const string Usage = "Usage: /maintenance <on [minutes]|off|status> (minutes: 1-60)";
    private const string Unavailable = "World maintenance state is unavailable.";

    public string Name => "maintenance";
    public string[] Aliases => [];

    // The Admin flag itself, not AccessLevels.Admin, which also admits Console.
    public AccountAccessLevel RequiredAccess => AccountAccessLevel.Admin;

    public void Execute(CommandContext ctx, string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Then(Task.Run(() => repository.ReadAsync(worldId, CancellationToken.None)),
                persisted => ctx.Reply(persisted is null ? Unavailable : Describe(persisted)));
            return;
        }

        bool enabled;
        int minutes = 5;
        if (args.Length is 1 or 2 && string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            if (args.Length == 2 && (!int.TryParse(args[1], out minutes) || minutes is < 1 or > 60))
            {
                ctx.Reply(Usage);
                return;
            }
        }
        else if (args.Length == 1 && string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
        }
        else
        {
            ctx.Reply(Usage);
            return;
        }

        string actor = $"account:{ctx.Connection.AccountId?.Value}";
        var grace = TimeSpan.FromMinutes(minutes);
        ctx.Then(Task.Run(() => control.SetAsync(worldId, enabled, grace, actor, CancellationToken.None)),
            committed =>
            {
                if (committed is null)
                {
                    ctx.Reply(Unavailable);
                    return;
                }

                // On the tick: this world applies its own committed transition at once, without waiting for the
                // notification or the reconciliation.
                coordinator.ApplyCommitted(committed);
                ctx.Reply(Describe(committed));
            });
    }

    private static string Describe(WorldMaintenanceState state)
        => $"Maintenance {(state.Enabled ? "enabled" : "disabled")}, revision {state.Revision}, " +
           (state.DeadlineUtc is { } deadline
               ? $"deadline {deadline.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC."
               : "no deadline.");
}
