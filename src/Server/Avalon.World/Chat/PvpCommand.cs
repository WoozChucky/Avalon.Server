using Avalon.World.Pvp;

namespace Avalon.World.Chat;

/// <summary>
/// /pvp (#164): the same toggle as CMSG_PVP_TOGGLE, answered with SMSG_PVP_STATE. Any player may run
/// it. Synchronous, so it runs on the tick, inside the chat handler that dispatched it.
/// </summary>
public sealed class PvpCommand(PvpToggle toggle) : ICommand
{
    public string Name => "pvp";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args) => toggle.Toggle(ctx.Connection);
}
