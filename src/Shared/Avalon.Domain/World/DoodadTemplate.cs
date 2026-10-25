using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>How a player uses a doodad. Stored as a number, so values are only ever appended.</summary>
public enum DoodadInteraction : byte
{
    /// <summary>Scenery: nothing to use.</summary>
    None = 0,

    /// <summary>Used by clicking it, instantly or through a cast bar.</summary>
    Click = 1,
}

/// <summary>How often a doodad can be used. Stored as a number, so values are only ever appended.</summary>
public enum DoodadUseLimit : byte
{
    /// <summary>The first use spends it for everyone in the instance (until it respawns, if it does).</summary>
    OncePerInstance = 1,

    /// <summary>Never spent: every player may use it, each for themselves.</summary>
    Unlimited = 2,
}

/// <summary>What a spent doodad looks like. Stored as a number, so values are only ever appended.</summary>
public enum DoodadSpentLook : byte
{
    /// <summary>It stays, in its used look (UsedModelKey or UsedClip).</summary>
    ShowUsed = 1,

    /// <summary>It disappears.</summary>
    Despawn = 2,
}

/// <summary>Whether a doodad blocks movement. Stored as a number, so values are only ever appended.</summary>
public enum DoodadCollision : byte
{
    /// <summary>Walk-through.</summary>
    None = 0,

    /// <summary>Solid: an upright box of the template's half extents and height, on the doodad's facing.</summary>
    Box = 1,
}

/// <summary>
/// The service window a click on a doodad opens on the client. Stored as a number, so values are only ever appended.
/// </summary>
public enum DoodadService : byte
{
    /// <summary>The Resonance Workbench: identifies the user's unidentified items, one per request.</summary>
    Identify = 1,
}

/// <summary>
/// A quest state a doodad's visibility or usability asks of the viewer's quest. Stored as a number, so values are only
/// ever appended.
/// </summary>
public enum DoodadGateCondition : byte
{
    /// <summary>The quest was never taken.</summary>
    NotStarted = 1,

    /// <summary>The quest is active and the objective named beside it is still open.</summary>
    OnObjective = 2,

    /// <summary>The quest was turned in.</summary>
    Completed = 3,

    /// <summary>The quest was not turned in: never taken, or active.</summary>
    NotCompleted = 4,
}

/// <summary>
/// An interactable world object's template: a chest, a barrel, a shrine, a fountain, a quest object. Reference data;
/// ids are chosen by whoever authors the data, not generated. Check constraints refuse what the world could not run,
/// and the world's catalog validates the rest on load (plain asset names, the gate quests and objectives, the script).
/// </summary>
public class DoodadTemplate : IDbEntity<DoodadTemplateId>
{
    public DoodadTemplateId Id { get; set; } = null!;

    /// <summary>A label for whoever authors doodads.</summary>
    public string Name { get; set; } = "";

    /// <summary>The client's model kit folder, a plain asset name.</summary>
    public string ModelKit { get; set; } = "";

    /// <summary>The client's model name inside the kit, a plain asset name.</summary>
    public string ModelKey { get; set; } = "";

    /// <summary>The model shown once spent, a plain asset name; null keeps ModelKey.</summary>
    public string? UsedModelKey { get; set; }

    /// <summary>The animation clip played while ready, a plain asset name, or null.</summary>
    public string? ReadyClip { get; set; }

    /// <summary>The animation clip played on use, a plain asset name, or null.</summary>
    public string? UseClip { get; set; }

    /// <summary>The animation clip held once spent, a plain asset name, or null.</summary>
    public string? UsedClip { get; set; }

    public DoodadInteraction Interaction { get; set; }

    /// <summary>The cast bar in milliseconds; 0 is an instant use.</summary>
    public uint InteractionTimeMs { get; set; }

    /// <summary>How near a player must stand to use it, in metres: above 0, at most 10.</summary>
    public float InteractionRange { get; set; } = 5f;

    /// <summary>The radius its effects reach around it, in metres: 0 (the user alone) to 30.</summary>
    public float AreaRadius { get; set; }

    public DoodadUseLimit UseLimit { get; set; }

    public DoodadSpentLook SpentLook { get; set; }

    /// <summary>Seconds from spent to ready again; null never respawns.</summary>
    public uint? RespawnSeconds { get; set; }

    /// <summary>The DoodadScript run for it, by class name, or null.</summary>
    public string? ScriptName { get; set; }

    /// <summary>What it drops on use, or null.</summary>
    public LootTableId? LootTableId { get; set; }

    /// <summary>Gold dropped on use, rolled from MinGold to MaxGold; both 0 drops none.</summary>
    public int MinGold { get; set; }

    public int MaxGold { get; set; }

    /// <summary>Health and power restored on use, in percent of the maximum, to everyone in AreaRadius: 0 to 100.</summary>
    public uint RestorePct { get; set; }

    public DoodadCollision Collision { get; set; }

    /// <summary>A solid doodad's box: half its width along its facing's X, in metres.</summary>
    public float CollisionHalfX { get; set; }

    /// <summary>A solid doodad's box: half its depth along its facing's Z, in metres.</summary>
    public float CollisionHalfZ { get; set; }

    /// <summary>A solid doodad's box height, in metres, at least 1.05.</summary>
    public float CollisionHeight { get; set; }

    /// <summary>
    /// A QuestTemplate id whose state, with VisibleCondition, decides who sees it; null for everyone. No foreign key:
    /// the catalog checks it, so seed order never matters.
    /// </summary>
    public uint? VisibleQuestId { get; set; }

    /// <summary>Set exactly when VisibleQuestId is.</summary>
    public DoodadGateCondition? VisibleCondition { get; set; }

    /// <summary>The objective an OnObjective visibility gate names.</summary>
    public uint? VisibleObjectiveId { get; set; }

    /// <summary>A QuestTemplate id whose state, with UsableCondition, decides who may use it; null for everyone.</summary>
    public uint? UsableQuestId { get; set; }

    /// <summary>Set exactly when UsableQuestId is.</summary>
    public DoodadGateCondition? UsableCondition { get; set; }

    /// <summary>The objective an OnObjective usability gate names.</summary>
    public uint? UsableObjectiveId { get; set; }

    /// <summary>
    /// The service window a click opens on the client, or null for none. The catalog keeps an Identify doodad clicked and
    /// Unlimited, with no script, loot table or gate, so it is never hidden, spent or despawned.
    /// </summary>
    public DoodadService? Service { get; set; }

    /// <summary>The auras one use may apply, one drawn by weight; none applies no aura.</summary>
    public List<DoodadAuraOutcome> AuraOutcomes { get; set; } = [];
}

/// <summary>One aura a doodad's use may apply, keyed by (DoodadTemplateId, AuraId), drawn with its weight.</summary>
public class DoodadAuraOutcome
{
    public DoodadTemplateId DoodadTemplateId { get; set; } = null!;

    public AuraId AuraId { get; set; } = null!;

    /// <summary>Its share of the draw, at least 1.</summary>
    public uint Weight { get; set; }
}
