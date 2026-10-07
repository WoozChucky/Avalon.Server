using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using WorldEntity = Avalon.Domain.Auth.World;

namespace Avalon.Api.Hosting.Worlds;

/// <summary>
/// Which worlds an anonymous-friendly endpoint may read for a caller, and which one it uses when none is
/// named. Shared by <c>GET /public/world</c> and the link previews, so both choose the same default.
/// </summary>
public static class PublicWorlds
{
    /// <summary>The configured, available worlds whose access rule admits the caller, in configured order.</summary>
    public static async Task<List<WorldEntity>> ReadableAsync(IWorldRepository worlds, IWorldDatabases databases,
        AccountAccessLevel caller, CancellationToken ct)
    {
        List<WorldEntity> readable = [];
        foreach (ConfiguredWorld configured in databases.All)
        {
            if (!databases.IsAvailable(configured.Id)) continue;
            WorldEntity? world = await worlds.FindByIdAsync(configured.Id, track: false, ct);
            if (world is null || !AccessLevels.ForWorld(world.AccessLevelRequired).Allows(caller)) continue;
            readable.Add(world);
        }

        return readable;
    }

    /// <summary>The configured world when readable, else the first readable one.</summary>
    public static ushort? DefaultOf(IReadOnlyList<WorldEntity> readable, ushort? configured) =>
        readable.Any(w => w.Id.Value == configured) ? configured : readable.FirstOrDefault()?.Id.Value;
}
