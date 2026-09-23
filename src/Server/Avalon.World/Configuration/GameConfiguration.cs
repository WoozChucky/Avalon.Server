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

    /// <summary>
    ///     Which implementation moves creatures. <see cref="CreatureLocomotionMode.Waypoint" /> is
    ///     what the world has always done; <see cref="CreatureLocomotionMode.Crowd" /> steers them
    ///     with DotRecast so they avoid each other.
    /// </summary>
    public CreatureLocomotionMode CreatureLocomotion { get; set; } = CreatureLocomotionMode.Waypoint;

    /// <summary>
    ///     Whether players are registered as crowd agents, so creatures steer around them. Ignored
    ///     under <see cref="CreatureLocomotionMode.Waypoint" />.
    ///     <para>
    ///     Off by default because it makes body-blocking real: a player standing in a doorway
    ///     becomes something creatures path around, which is a gameplay change rather than a fix.
    ///     </para>
    /// </summary>
    public bool CrowdIncludesPlayers { get; set; }

    /// <summary>
    ///     Agent radius in world units, used for separation. One value for every creature until
    ///     per-creature radii exist on the template.
    /// </summary>
    [Range(0.05, 10.0)]
    public float CreatureAgentRadius { get; set; } = 0.5f;

    /// <summary>
    ///     How many creatures can surround one target before the surplus falls back to piling on
    ///     its centre. Bounded by the ring's circumference: at the default radius there are ~9.42
    ///     units to share, so six slots leave 1.57 between centres against a 1.0 agent diameter,
    ///     and eight leave only 0.18 of margin.
    /// </summary>
    [Range(1, 16)]
    public int MeleeSlotCount { get; set; } = 6;

    /// <summary>
    ///     Radius of the ring creatures surround a target on. Must not exceed
    ///     <c>CreatureCombatScript.AttackRange</c>, or creatures stand where they cannot reach.
    /// </summary>
    [Range(0.5, 20.0)]
    public float MeleeSlotRadius { get; set; } = 1.5f;
}
