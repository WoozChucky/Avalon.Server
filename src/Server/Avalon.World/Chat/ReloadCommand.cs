using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

/// <summary>
/// /reload &lt;area&gt; — applies changed content to the running server. Forward-only: it changes what
/// is built afterwards, never creatures already spawned or abilities already built at login.
/// </summary>
public sealed class ReloadCommand(IReferenceDataReloader reloader, ILogger<ReloadCommand> logger) : ICommand
{
    private const string Usage = "Usage: /reload <dialogue|creatures|abilities|items|progression|loot|all>";

    private const string MapsRefusal =
        "Maps and chunk layouts cannot be reloaded: live instances have already baked a navmesh " +
        "from them. Restart the world server.";

    public string Name => "reload";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public async Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args,
        CancellationToken token = default)
    {
        string requested = args.Length == 0 ? string.Empty : args[0].ToLowerInvariant();

        if (requested is "maps" or "chunks")
        {
            Reply(ctx, MapsRefusal);
            return;
        }

        IReadOnlyList<ReloadArea>? areas = requested switch
        {
            "all" => Enum.GetValues<ReloadArea>(),
            _ when Enum.TryParse(requested, ignoreCase: true, out ReloadArea area) && Enum.IsDefined(area) => [area],
            _ => null
        };

        if (areas is null)
        {
            Reply(ctx, Usage);
            return;
        }

        ReloadReport report = await reloader.ReloadAsync(areas, token);

        logger.LogInformation("Account {AccountId} reloaded {Areas}: {Outcomes}",
            ctx.Connection.AccountId, string.Join(",", areas),
            string.Join(", ", report.Outcomes.Select(o => $"{o.Area}={(o.Succeeded ? "ok" : "failed")}")));

        foreach (ReloadOutcome outcome in report.Outcomes)
        {
            Reply(ctx, Describe(outcome));
        }
    }

    private static string Describe(ReloadOutcome outcome)
    {
        string area = outcome.Area.ToString().ToLowerInvariant();

        if (!outcome.Succeeded)
        {
            return $"Reload of {area} failed: {outcome.Error?.GetType().Name}. Nothing changed.";
        }

        int ms = (int)Math.Round(outcome.Elapsed.TotalMilliseconds);
        string line = $"Reloaded {area}: {outcome.Summary} ({ms} ms).";

        // Forward-only: without these a game master reloads, looks at something already in the
        // world, and concludes the reload failed.
        return outcome.Area switch
        {
            ReloadArea.Creatures => line + " Affects new spawns only.",
            ReloadArea.Loot => line + " Affects the next kill; drops already on the ground keep what they rolled.",
            _ => line
        };
    }

    private static void Reply(WorldPacketContext<CChatMessagePacket> ctx, string message)
    {
        // Safe off the tick thread: both outboxes are created with SingleWriter = false.
        ctx.Connection.Send(SChatMessagePacket.Create(
            0UL, 0UL, "System", message, ctx.Packet.DateTime, ctx.Connection.CryptoSession.Encrypt));
    }
}
