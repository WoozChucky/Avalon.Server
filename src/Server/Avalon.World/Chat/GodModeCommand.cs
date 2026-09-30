using Avalon.Common.Accounts;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

public sealed class GodModeCommand(ILogger<GodModeCommand> logger) : ICommand
{
    public string Name => "god";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public void Execute(CommandContext ctx, string[] args)
    {
        if (args.Length != 1 ||
            !string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Reply("Usage: /god <on|off>");
            return;
        }

        if (ctx.Connection.Character is not CharacterEntity character)
        {
            ctx.Reply("No character selected.");
            return;
        }

        bool enable = string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase);
        if (enable && character.IsDead)
        {
            ctx.Reply("Cannot enable god mode while dead.");
            return;
        }

        if (character.GodMode == enable)
        {
            ctx.Reply(enable ? "God mode is already enabled." : "God mode is already disabled.");
            return;
        }

        character.GodMode = enable;
        logger.LogInformation("Account {AccountId} set god mode {Enabled} for character {CharacterId}",
            ctx.Connection.AccountId, enable, character.Guid);
        ctx.Reply(enable ? "God mode enabled." : "God mode disabled.");
    }
}
