using System.Globalization;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

/// <summary>
/// <c>/aura &lt;aura id&gt; [seconds]</c> (GameMaster): applies an aura to the caller from nobody, so no combat is
/// entered and no diminishing step recorded; <c>seconds</c> (up to <see cref="MaxSeconds" />) replaces the row's
/// duration exactly, past control resistance and diminishing. <c>/aura clear</c> ends the caller's harmful auras. A
/// testing tool: the caller's own character under crowd control without a second player.
/// </summary>
public sealed class AuraCommand(IWorld world, ILogger<AuraCommand> logger) : ICommand
{
    public const decimal MaxSeconds = 600m;

    private const string Usage = "Usage: /aura <aura id> [seconds, up to 600] or /aura clear";

    public string Name => "aura";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public void Execute(CommandContext ctx, string[] args)
    {
        bool clear = args.Length == 1 && string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase);
        uint auraId = 0;
        uint? durationMs = null;
        if (!clear && !TryParse(args, out auraId, out durationMs))
        {
            ctx.Reply(Usage);
            return;
        }

        if (ctx.Connection.Character is not CharacterEntity character)
        {
            ctx.Reply("No character selected.");
            return;
        }

        if (world.InstanceRegistry.GetInstanceById(character.InstanceId) is not IAuraHost host)
        {
            ctx.Reply("You are not in a map.");
            return;
        }

        if (clear)
        {
            Clear(ctx, character, host.Auras);
            return;
        }

        if (!host.Auras.Catalog.TryGet(new AuraId(auraId), out AuraTemplate? template))
        {
            ctx.Reply($"No aura has id {auraId}.");
            return;
        }

        AuraApplyResult result = host.Auras.Apply(null, character, template, AuraSource.None, durationMs);
        logger.LogInformation("Account {AccountId} applied aura {AuraId} ({Aura}, {DurationMs} ms) to character {CharacterId}: {Result}",
            ctx.Connection.AccountId, auraId, template.Name, durationMs, character.Guid, result);
        ctx.Reply(Answer(character, template, result));
    }

    private void Clear(CommandContext ctx, CharacterEntity character, AuraSystem auras)
    {
        int harmful = character.Auras.All.Count(a => a.Template.Kind == AuraKind.Harmful);
        auras.RemoveHarmful(character, AuraRemoveReason.Cancelled);
        logger.LogInformation("Account {AccountId} cleared {Count} harmful auras from character {CharacterId}",
            ctx.Connection.AccountId, harmful, character.Guid);
        ctx.Reply(harmful == 0
            ? "You hold no harmful auras."
            : $"Removed {harmful} harmful aura{(harmful == 1 ? "" : "s")}.");
    }

    // Refused names the first of Apply's refusals that holds now; each of them still holds once Apply has returned.
    private static string Answer(CharacterEntity character, AuraTemplate template, AuraApplyResult result)
    {
        string aura = $"{template.Name} ({template.Id.Value})";
        switch (result)
        {
            case AuraApplyResult.Refused when character.IsDead:
                return $"{aura} was refused: you are dead.";
            case AuraApplyResult.Refused when template.Kind == AuraKind.Harmful && ArrivalProtection.IsProtected(character):
                return $"{aura} was refused: arrival protection passes harmful auras over until you move or act.";
            case AuraApplyResult.Refused:
                return $"{aura} was refused: you hold the most auras Game:MaxAurasPerUnit allows.";
            case AuraApplyResult.Resisted:
                return $"{aura} was resisted: its crowd control lands for no time on you.";
        }

        // A script may end its aura from its first hook, so the copy held is asked rather than the duration given.
        string verb = result switch
        {
            AuraApplyResult.Stacked => "Stacked",
            AuraApplyResult.Refreshed => "Refreshed",
            _ => "Applied",
        };
        return character.Auras.Find(template.Id, new ObjectGuid()) is { } held
            ? FormattableString.Invariant($"{verb} {aura} for {held.DurationMs / 1000.0:0.###} s.")
            : $"{verb} {aura}; its script ended it at once.";
    }

    // The id alone, read as a plain number; then the seconds, a decimal with a dot on any culture, in whole milliseconds.
    private static bool TryParse(string[] args, out uint auraId, out uint? durationMs)
    {
        auraId = 0;
        durationMs = null;
        if (args.Length is < 1 or > 2 ||
            !uint.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out auraId))
        {
            return false;
        }

        if (args.Length == 1)
            return true;

        if (!decimal.TryParse(args[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal seconds) ||
            seconds is <= 0m or > MaxSeconds)
        {
            return false;
        }

        // A positive time too small to be a whole millisecond is no duration.
        uint ms = (uint)Math.Round(seconds * 1000m, MidpointRounding.AwayFromZero);
        if (ms == 0)
            return false;
        durationMs = ms;
        return true;
    }
}
