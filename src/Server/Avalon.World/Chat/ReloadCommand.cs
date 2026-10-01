using Avalon.Common.Accounts;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

/// <summary>
/// /reload &lt;area&gt; — applies changed content to the running server. Forward-only: it changes what
/// is built afterwards, never creatures already spawned or abilities already built at login.
/// </summary>
public sealed class ReloadCommand(IReferenceDataReloader reloader, ILogger<ReloadCommand> logger) : ICommand
{
    private const string Usage = "Usage: /reload <dialogue|creatures|abilities|items|progression|loot|vendors|combat|quests|all>";

    private const string MapsRefusal =
        "Maps and chunk layouts cannot be reloaded: live instances have already baked a navmesh " +
        "from them. Restart the world server.";

    public string Name => "reload";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public void Execute(CommandContext ctx, string[] args)
    {
        string requested = args.Length == 0 ? string.Empty : args[0].ToLowerInvariant();

        if (requested is "maps" or "chunks")
        {
            ctx.Reply(MapsRefusal);
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
            ctx.Reply(Usage);
            return;
        }

        // The reload reads the database off the tick; its report is answered on a later tick.
        ctx.Then(reloader.ReloadAsync(areas, CancellationToken.None), report =>
        {
            logger.LogInformation("Account {AccountId} reloaded {Areas}: {Outcomes}",
                ctx.Connection.AccountId, string.Join(",", areas),
                string.Join(", ", report.Outcomes.Select(o => $"{o.Area}={(o.Succeeded ? "ok" : "failed")}")));

            foreach (ReloadOutcome outcome in report.Outcomes)
            {
                ctx.Reply(Describe(outcome));
            }
        });
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
            ReloadArea.Vendors => line + " Open shops get the new list on the next tick; live stock counts carry over by row.",
            ReloadArea.Combat => line + " Affects the next hit; a character's stats change at its next select, gear change or level-up.",
            ReloadArea.Quests => line + " Affects what is offered and credited from the next tick; characters keep the quests they hold.",
            _ => line
        };
    }
}
