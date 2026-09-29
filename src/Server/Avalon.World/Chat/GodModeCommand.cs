using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;
using Avalon.World.Entities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Chat;

public sealed class GodModeCommand(ILogger<GodModeCommand> logger) : ICommand
{
    public string Name => "god";
    public string[] Aliases => [];
    public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;

    public Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args,
        CancellationToken token = default)
    {
        if (args.Length != 1 ||
            !string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase))
        {
            Reply(ctx, "Usage: /god <on|off>");
            return Task.CompletedTask;
        }

        if (ctx.Connection.Character is not CharacterEntity character)
        {
            Reply(ctx, "No character selected.");
            return Task.CompletedTask;
        }

        bool enable = string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase);
        if (enable && character.IsDead)
        {
            Reply(ctx, "Cannot enable god mode while dead.");
            return Task.CompletedTask;
        }

        if (character.GodMode == enable)
        {
            Reply(ctx, enable ? "God mode is already enabled." : "God mode is already disabled.");
            return Task.CompletedTask;
        }

        character.GodMode = enable;
        logger.LogInformation("Account {AccountId} set god mode {Enabled} for character {CharacterId}",
            ctx.Connection.AccountId, enable, character.Guid);
        Reply(ctx, enable ? "God mode enabled." : "God mode disabled.");
        return Task.CompletedTask;
    }

    private static void Reply(WorldPacketContext<CChatMessagePacket> ctx, string message) =>
        ctx.Connection.Send(SChatMessagePacket.Create(
            0UL, 0UL, "System", message, ctx.Packet.DateTime, ctx.Connection.CryptoSession.Encrypt));
}
