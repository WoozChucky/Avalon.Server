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
    ///     How often the world tick asks <c>IScriptHotReloader</c> whether any AI script
    ///     changed on disk. Polling, so this is the worst-case delay between saving a script and the
    ///     world picking it up — and the cost of a shorter interval is paid every tick.
    /// </summary>
    [Range(1, 3600)]
    public int ScriptHotReloadIntervalSeconds { get; set; } = 5;

    /// <summary>
    ///     The world's own player limit for its backstop (#967), 1 to 100,000; null (the default) for no backstop. Set
    ///     to the same number as the world's stored limit (<c>PUT /world/{id}/player-limit</c>), which identity enforces
    ///     with the queue and <c>WORLD_FULL</c>; this one only guards the pod if identity's place ledger is wrong or lost.
    ///     Read at startup: a change rolls the pod.
    /// </summary>
    [Range(1, 100_000)]
    public int? PlayerLimit { get; set; }

    /// <summary>
    ///     The backstop's margin over the world's player limit (#967), in percent of the limit; the margin is at least
    ///     <see cref="PlayerLimitMarginMinimum" />. It absorbs staff and timing, so in normal operation it never fires.
    /// </summary>
    [Range(0, 100)]
    public int PlayerLimitMarginPercent { get; set; } = 5;

    /// <summary>
    ///     The backstop's smallest margin over the player limit, in accounts (#967). By default the admission lane's
    ///     concurrency (<see cref="Persistence.WorldDatabaseWork.AdmissionConcurrency" />, 32): admissions that run at
    ///     once do not see each other. Admissions passed within one tick, before the tick publishes them, can add about a
    ///     hundred more under a reconnect storm, which the percentage covers from a limit of about 640
    ///     (docs/world-player-limit.md, the backstop).
    /// </summary>
    [Range(0, 100_000)]
    public int PlayerLimitMarginMinimum { get; set; } = Persistence.WorldDatabaseWork.AdmissionConcurrency;

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
    ///     The most gold a character can hold. An addition that would pass it is refused whole,
    ///     rather than clamped. The default is large enough that normal play never reaches it and
    ///     well within what the database column holds.
    /// </summary>
    public ulong MaxGold { get; set; } = 9_999_999_999;

    /// <summary>
    ///     How long a drop stays reserved for the character it was allocated to before anyone in the
    ///     instance may take it (issue #460). No visible effect yet: every drop goes to its instance's
    ///     owner, who is the only character there until groups exist.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
    public TimeSpan LootGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Metres. How close a character must stand to a drop to pick it up. The same as the range for
    ///     talking to an NPC.
    /// </summary>
    [Range(0.5, 50.0)]
    public float LootPickupRange { get; set; } = 5f;

    /// <summary>
    ///     How long a PvP flag stays on after its owner asks to turn it off (#164). Any player-on-player
    ///     hit restarts a running timer at this length, for both players.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PvpOffDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     How often every in-world character is saved (spec #459, D4). Each character's first save is
    ///     staggered inside one interval by its id, so characters that entered together do not all
    ///     save on the same tick.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:10", "01:00:00")]
    public TimeSpan CharacterSaveInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Metres, on X/Z. A character, creature or projectile is added to a client's view within this
    ///     distance of the client's own character (#593), and a one-shot effect broadcast (a hit, a cast,
    ///     an ability fired, a swing, a death, a revive) goes only to connections within it of the effect
    ///     or involved in it (#532). At least 1 and finite: the range refuses NaN and both infinities
    ///     too, since neither compares inside it.
    /// </summary>
    [Range(1.0, double.MaxValue)]
    public float InterestRadius { get; set; } = 60f;

    /// <summary>
    ///     Metres added to <see cref="InterestRadius" /> before an object already in a client's view is
    ///     removed from it (#593), so one standing near the edge does not flicker in and out. 0 or more
    ///     and finite; 0 turns the margin off.
    /// </summary>
    [Range(0.0, double.MaxValue)]
    public float InterestRemoveMargin { get; set; } = 10f;

    /// <summary>The most characters in one party (2026-09-30). A party instance also holds at most this many.</summary>
    [Range(2, 40)]
    public int MaxPartySize { get; set; } = 6;

    /// <summary>
    /// Minutes an abandoned Normal map instance (a dungeon, solo or a party's) lives once its last player has left:
    /// re-entry within it returns the same instance, and the per-tick expiry pass frees it after. 0 frees it on the next
    /// tick, so the next entry builds a new one. An instance nobody has entered yet keeps a fixed 15 minutes, and towns
    /// never expire. Negative is refused at start.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int AbandonedInstanceLifetimeMinutes { get; set; } = DefaultAbandonedInstanceLifetimeMinutes;

    /// <summary>
    /// Seconds after a character opens a new instance at a portal, a private or a party one (#1104), before it may open
    /// another. Counted from when the create is accepted, so a build that fails still costs it; entering an instance
    /// that is already live is never held back. Kept in memory only, per character: a new session starts without one.
    /// 0 turns it off.
    /// </summary>
    [Range(0, 3600)]
    public int InstanceCreateCooldownSeconds { get; set; } = 10;

    /// <summary>Seconds an invite stays open before it expires.</summary>
    [Range(1, 3600)]
    public int PartyInviteTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How many party invites one character may send in any 60 seconds (#944): the invite packet and <c>/invite</c>
    /// share the one budget, which limits invite spam. Every invite that looks its target up counts, whatever it finds;
    /// one past the budget is answered <c>TooFast</c> and looks nothing up. 0 or below turns the limit off. The default
    /// is in <c>appsettings.json</c>.
    /// </summary>
    [Range(int.MinValue, 10_000)]
    public int PartyInvitesPerMinute { get; set; }

    /// <summary>Seconds a trade request stays open before it expires (#1175).</summary>
    [Range(1, 3600)]
    public int TradeRequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// How many trade requests one character may send in any 60 seconds (#1175), which limits request spam. Every
    /// request that looks its target up counts, whatever it finds; one past the budget is answered <c>TooFast</c> and
    /// looks nothing up. 0 or below turns the limit off. The default is in <c>appsettings.json</c>.
    /// </summary>
    [Range(int.MinValue, 10_000)]
    public int TradeRequestsPerMinute { get; set; }

    /// <summary>Seconds a character who is no longer a member may stay in the party's instance before being moved to town.</summary>
    [Range(1, 3600)]
    public int PartyLeaveGraceSeconds { get; set; } = 60;

    /// <summary>
    /// Seconds before a leave countdown's return to town that failed is tried again (#700), up to
    /// PartyService.MaxReturnRetries times.
    /// </summary>
    [Range(1, 3600)]
    public int PartyReturnRetrySeconds { get; set; } = 5;

    /// <summary>Seconds after a switch of the experience mode before the leader may switch it again. 0 turns the wait off.</summary>
    [Range(0, 86400)]
    public int PartyExperienceModeCooldownSeconds { get; set; } = 60;

    /// <summary>Creature health added per player present beyond the first, as a share of its base: 0.6 is +60 %. 0 or more and finite.</summary>
    [Range(0.0, double.MaxValue)]
    public float PartyHealthPerExtraPlayer { get; set; } = 0.6f;

    /// <summary>Metres, on X/Z, from a corpse within which a member not in the creature's encounter still shares the kill. At least 1 and finite.</summary>
    [Range(1.0, double.MaxValue)]
    public float PartyEligibilityRange { get; set; } = 60f;

    /// <summary>
    /// Experience added to a kill per counted member beyond the first, as a share: 0.1 is +10 %. 0 to 10: the bound keeps
    /// the pool well inside what the experience split's decimal arithmetic can hold.
    /// </summary>
    [Range(0.0, 10.0)]
    public float PartyExperienceBonusPerExtra { get; set; } = 0.10f;

    /// <summary>A character this many levels or more above a creature gets no experience from it, solo or in a party.</summary>
    [Range(1, 1000)]
    public int PartyExperienceLevelGap { get; set; } = 5;

    /// <summary>How many quests a character may hold at once (#433). A full log refuses an accept with LogFull.</summary>
    [Range(1, 100)]
    public int MaxActiveQuests { get; set; } = 20;

    /// <summary>When daily, weekly and monthly quests come back (#710), UTC. Read at startup.</summary>
    public QuestResetConfiguration QuestResets { get; set; } = new();

    /// <summary>
    /// Game:Items (#1179): a made item's rarity odds by the dropping creature's rarity, and what a sale pays per rarity.
    /// Read at startup.
    /// </summary>
    public ItemConfiguration Items { get; set; } = new();

    /// <summary>How many characters one character may ignore (#723). A full list refuses /ignore with a system line.</summary>
    [Range(1, 500)]
    public int MaxIgnoredCharacters { get; set; } = 50;

    /// <summary>
    /// How many auras one unit may hold at once (auras). Past it a new aura is refused and logged; one the unit already
    /// holds still refreshes or stacks.
    /// </summary>
    [Range(1, 256)]
    public int MaxAurasPerUnit { get; set; } = 32;

    /// <summary>
    /// The PvP diminishing window (#770): a character's stun, root and silence from other characters step down (full,
    /// half, quarter, then immune) until no application of that type has landed on it for this long. Above 0 and at
    /// most 7 seconds.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:00.001", "00:00:07")]
    public TimeSpan CrowdControlDiminishWindow { get; set; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// How many player chat messages one character may send in any 60 seconds (#722): plain chat, <c>/p</c> and
    /// <c>/w</c> share the one budget. Only a delivered message counts. 0 or below turns the limit off. The default
    /// is in <c>appsettings.json</c>.
    /// </summary>
    [Range(int.MinValue, 10_000)]
    public int ChatMessagesPerMinute { get; set; }

    /// <summary>
    /// Turns on the tick-thread assertion (#639, <see cref="Threading.TickThreadGuard" />): world state that only the tick may
    /// change throws when changed from another thread. For development and tests; off by default, where each check
    /// costs one read of a flag. Read once, when the world server starts.
    /// </summary>
    public bool TickThreadGuard { get; set; }

    /// <summary>
    /// The CPU the tick thread runs on alone (#1002, <see cref="Threading.TickThreadPlacement" />), with the world's other
    /// threads kept off it and off its hyperthread siblings. Linux only: elsewhere the tick runs unpinned, with a warning.
    /// Unset (the default), nothing is pinned. Startup refuses a negative value, one above
    /// <see cref="Threading.TickThreadPlacement.MaxCpu" />, a CPU the process may not run on, and one whose core is every
    /// CPU the process has. Read once, when the tick thread starts.
    /// </summary>
    [Range(0, Threading.TickThreadPlacement.MaxCpu)]
    public int? TickThreadCpu { get; set; }

    /// <summary>The default of <see cref="AbandonedInstanceLifetimeMinutes" />, today's 15 minutes.</summary>
    public const int DefaultAbandonedInstanceLifetimeMinutes = 15;
}
