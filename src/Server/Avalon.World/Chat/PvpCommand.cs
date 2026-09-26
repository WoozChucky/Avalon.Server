using Avalon.Network.Packets.Social;
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

    public Task ExecuteAsync(WorldPacketContext<CChatMessagePacket> ctx, string[] args, CancellationToken token = default)
    {
        toggle.Toggle(ctx.Connection);
        return Task.CompletedTask;
    }
}
