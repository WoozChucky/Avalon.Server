using System.ComponentModel.DataAnnotations;
using Avalon.Domain.Auth;
using Avalon.World.Maps.Navigation;

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
    /// <remarks>
    ///     Defaults to the radius the navmesh was baked with. The bake erodes the walkable surface
    ///     by <see cref="NavmeshBuildSettings.AgentRadius" />, so a smaller value here would let
    ///     creatures pack closer to one another than the surface they stand on assumes they can,
    ///     and separation would disagree with the path corridor that produced it.
    /// </remarks>
    /// <summary>
    ///     Per-level multiplier applied to experience for each level the player sits outside the map's
    ///     level band. Symmetric, with no grace: one level out already costs 25% at the default.
    /// </summary>
    /// <remarks>
    ///     The band itself (<c>MapTemplate.MinLevel</c>/<c>MaxLevel</c>) constrains nothing about
    ///     spawning — a level 6 creature in a 1-5 map is legal. Its only job is scaling rewards.
    /// </remarks>
    [Range(0.01, 1.0)]
    public float ExperienceBandDecay { get; set; } = 0.75f;

    [Range(0.05, 10.0)]
    public float CreatureAgentRadius { get; set; } = NavmeshBuildSettings.AgentRadius;

    /// <summary>
    ///     How many creatures can surround one target before the surplus falls back to piling on
    ///     its centre. Bounded by the ring's circumference: at the default radius there are ~9.42
    ///     units to share, so six slots leave 1.57 between centres against a 1.2 agent diameter,
    ///     and eight leave 1.18 — less than one diameter, so they overlap.
    /// </summary>
    [Range(1, 16)]
    public int MeleeSlotCount { get; set; } = 6;

    /// <summary>
    ///     Radius of the ring creatures surround a target on. Must not exceed
    ///     <c>CreatureCombatScript.AttackRange</c> (plus its small arrival tolerance) — if it
    ///     does, a creature that arrives at its claimed slot is standing outside attack range and
    ///     never attacks, parking there indefinitely instead. Not enforced with
    ///     <see cref="RangeAttribute" /> because <c>AttackRange</c> is a const inside the combat
    ///     script, not a value this type can see.
    /// </summary>
    [Range(0.5, 20.0)]
    public float MeleeSlotRadius { get; set; } = 1.5f;

    /// <summary>
    ///     The most copper a character can hold. An addition that would pass it is refused whole,
    ///     as TrinityCore refuses past <c>MAX_MONEY_AMOUNT</c>, whose 3.3.5 value is the default.
    /// </summary>
    public ulong MaxMoney { get; set; } = 9_999_999_999;
}
