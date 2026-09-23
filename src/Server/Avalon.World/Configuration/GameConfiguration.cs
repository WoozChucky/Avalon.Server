using System.ComponentModel.DataAnnotations;
using Avalon.Domain.Auth;

namespace Avalon.World.Configuration;

public class GameConfiguration
{
    public WorldId WorldId { get; set; }
    public ushort MaxCharactersPerAccount { get; set; }
    public float PlayerRadius { get; set; }

    /// <summary>
    ///     How long a selected character waits for its client to send <c>CMSG_CHARACTER_LOADED</c>
    ///     before the world tick spawns it anyway. The bound on a client that never reports in;
    ///     a client that does is not made to wait any of it.
    /// </summary>
    [Range(1, 300)]
    public int CharacterLoadTimeoutSeconds { get; set; } = 15;

    /// <summary>
    ///     How often the world tick asks <c>IScriptHotReloader</c> whether any AI or spell script
    ///     changed on disk. Polling, so this is the worst-case delay between saving a script and the
    ///     world picking it up — and the cost of a shorter interval is paid every tick.
    /// </summary>
    [Range(1, 3600)]
    public int ScriptHotReloadIntervalSeconds { get; set; } = 5;
}
