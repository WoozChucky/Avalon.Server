using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Microsoft.Extensions.Options;

namespace Avalon.World.Social;

/// <summary>/ignorelist (#723): the caller's ignore list as one system line, oldest entry first.</summary>
public sealed class IgnoreListCommand(IOptions<GameConfiguration> options) : ICommand
{
    public string Name => "ignorelist";
    public string[] Aliases => [];

    public void Execute(CommandContext ctx, string[] args)
    {
        if (ctx.Connection.Character is not CharacterEntity owner)
            return;

        ctx.Reply(owner.Ignores.Count == 0
            ? IgnoreLines.Empty
            : IgnoreLines.Listing(owner.Ignores, options.Value.MaxIgnoredCharacters));
    }
}
