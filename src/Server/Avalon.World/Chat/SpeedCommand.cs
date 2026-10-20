using System.Globalization;
using Avalon.Common.Accounts;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

public sealed class SpeedCommand(ILogger<SpeedCommand> logger) : ICommand
{
    public string Name => "speed";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public void Execute(CommandContext ctx, string[] args)
    {
        float multiplier = 1f;
        if (args.Length > 1 || args.Length == 1 &&
            (!float.TryParse(args[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out multiplier) ||
             !float.IsFinite(multiplier) || multiplier is < 1f or > 10f))
        {
            ctx.Reply("Usage: /speed [multiplier 1-10] (no argument or 1 resets)");
            return;
        }

        if (ctx.Connection.Character is not CharacterEntity character)
        {
            ctx.Reply("No character selected.");
            return;
        }

        character.MovementSpeedMultiplier = multiplier;
        float speed = character.GetMovementSpeed();
        logger.LogInformation("Account {AccountId} set movement speed multiplier {Multiplier} ({Speed} m/s) for character {CharacterId}",
            ctx.Connection.AccountId, multiplier, speed, character.Guid);
        ctx.Reply(FormattableString.Invariant($"Movement speed: {multiplier}x ({speed} m/s)."));
    }
}
