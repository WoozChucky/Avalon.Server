using System.Diagnostics.CodeAnalysis;
using Avalon.Common.Utils;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Loot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Doodads;

/// <summary>A quest state a doodad asks of its viewer: the quest, the condition and, for OnObjective, the objective.</summary>
public readonly record struct DoodadGate(uint QuestId, DoodadGateCondition Condition, uint? ObjectiveId);

/// <summary>One aura a use may apply, drawn with its weight out of the template's OutcomeWeightTotal.</summary>
public sealed record DoodadOutcomeView(AuraId AuraId, uint Weight);

/// <summary>
/// A doodad template that passed validation: its data, its gates, its script resolved and its outcomes summed. Built
/// once per load and immutable afterwards.
/// </summary>
/// <param name="ScriptCanSee">
/// Whether its script declares its own CanSee (DoodadScripts.OverridesCanSee), so a view decision asks it; always false
/// for a solid template.
/// </param>
public sealed record DoodadTemplateView(
    DoodadTemplateId Id, string Name, string ModelKit, string ModelKey, string? UsedModelKey,
    string? ReadyClip, string? UseClip, string? UsedClip,
    DoodadInteraction Interaction, uint InteractionTimeMs, float InteractionRange, float AreaRadius,
    DoodadUseLimit UseLimit, DoodadSpentLook SpentLook, uint? RespawnSeconds, Type? ScriptType,
    LootTableId? LootTableId, int MinGold, int MaxGold, uint RestorePct,
    DoodadCollision Collision, float CollisionHalfX, float CollisionHalfZ, float CollisionHeight,
    DoodadGate? Visible, DoodadGate? Usable, IReadOnlyList<DoodadOutcomeView> AuraOutcomes, uint OutcomeWeightTotal,
    bool ScriptCanSee)
{
    /// <summary>It blocks movement, so it is never hidden from anyone: no gate, and its script's CanSee is ignored.</summary>
    public bool Solid => Collision != DoodadCollision.None;
}

/// <summary>A template the catalog left out, and the first rule it broke.</summary>
public sealed record DoodadRefusal(uint TemplateId, string Reason)
{
    public override string ToString() => $"doodad {TemplateId}: {Reason}";
}

/// <summary>
/// Every doodad template that passed validation. Built off the tick thread by the Doodads reload area, against the loot
/// tables, auras and quests read in the same prepare, and immutable afterwards. A bad template is left out with an
/// error naming it, and every other one still loads.
/// </summary>
public sealed class DoodadCatalog
{
    /// <summary>The longest cast bar a doodad may have, in milliseconds.</summary>
    public const uint MaxInteractionTimeMs = 60_000;

    /// <summary>The farthest a player may stand from a doodad to use it, in metres.</summary>
    public const float MaxInteractionRange = 10f;

    /// <summary>The farthest a doodad's effects reach, in metres.</summary>
    public const float MaxAreaRadius = 30f;

    /// <summary>
    /// The lowest a solid doodad's box may stand, in metres: the navmesh climbs anything lower, so a player could walk
    /// over it. Compared as a float, so a height stored as 1.05f passes.
    /// </summary>
    public const float MinSolidHeight = 1.05f;

    private readonly Dictionary<uint, DoodadTemplateView> _templates = [];

    /// <summary>No doodad at all: what a world sees before its first load, and what a test without doodads reads.</summary>
    public static DoodadCatalog Empty { get; } = new([], new LootCatalog([], NullLoggerFactory.Instance), [], [],
        static _ => null, NullLoggerFactory.Instance);

    /// <param name="findScript">The DoodadScript type a script name names, or null: ScriptManager.GetDoodadScript.</param>
    public DoodadCatalog(IReadOnlyCollection<DoodadTemplate> templates, LootCatalog loot,
        IReadOnlyCollection<AuraTemplate> auras, IReadOnlyCollection<QuestTemplate> quests,
        Func<string, Type?> findScript, ILoggerFactory loggerFactory)
    {
        ILogger<DoodadCatalog> logger = loggerFactory.CreateLogger<DoodadCatalog>();
        var auraIds = auras.Select(a => a.Id.Value).ToHashSet();
        var objectivesByQuest = quests.ToDictionary(q => q.Id.Value,
            q => q.Objectives.Select(o => o.Id).ToHashSet());
        List<DoodadRefusal> refused = [];

        foreach (DoodadTemplate template in templates.OrderBy(t => t.Id?.Value ?? 0))
        {
            uint id = template.Id?.Value ?? 0;
            Type? scriptType = template.ScriptName is { Length: > 0 } script ? findScript(script) : null;
            string? reason = Problem(template, scriptType, loot, auraIds, objectivesByQuest);
            if (reason is not null)
            {
                refused.Add(new DoodadRefusal(id, reason));
                continue;
            }

            bool scriptCanSee = scriptType is not null && DoodadScripts.OverridesCanSee(scriptType);
            if (scriptCanSee && template.Collision != DoodadCollision.None)
            {
                // A solid doodad is never hidden, so a script cannot hide one either.
                logger.LogWarning("Doodad {DoodadTemplateId} '{Name}' is solid: its script {Script}'s CanSee is ignored, " +
                                  "and everyone sees it", id, template.Name, scriptType!.Name);
                scriptCanSee = false;
            }

            _templates[id] = View(template, scriptType, scriptCanSee);
        }

        Refused = refused;
        // In id order whatever order the dictionary keeps: the export and every walk of the catalog read it so.
        Templates = _templates.Values.OrderBy(t => t.Id.Value).ToList();

        foreach (DoodadRefusal refusal in Refused)
        {
            logger.LogError("Refused doodad {DoodadTemplateId}: {Reason}. Nothing places it", refusal.TemplateId,
                refusal.Reason);
        }

        if (templates.Count > 0)
            logger.LogInformation("Loaded {Count} doodads; refused {RefusedCount}", Templates.Count, Refused.Count);
    }

    /// <summary>The accepted templates, in id order.</summary>
    public IReadOnlyList<DoodadTemplateView> Templates { get; }

    public IReadOnlyList<DoodadRefusal> Refused { get; }

    public bool TryGet(DoodadTemplateId id, [NotNullWhen(true)] out DoodadTemplateView? template) =>
        _templates.TryGetValue(id.Value, out template);

    public string Describe() => $"{Templates.Count} doodads, {Refused.Count} refused";

    /// <summary>The first rule the template breaks, naming it, or null when it keeps every one.</summary>
    private static string? Problem(DoodadTemplate t, Type? scriptType, LootCatalog loot, HashSet<uint> auraIds,
        Dictionary<uint, HashSet<uint>> objectivesByQuest)
    {
        if (t.Id is null || t.Id.Value == 0)
            return "its id is 0";

        string name = $"'{t.Name}'";
        if (string.IsNullOrWhiteSpace(t.Name))
            return "its name is blank";

        if (NotPlain(t) is { } asset)
            return $"{name}: {asset} is not a plain asset name";

        if (UnknownEnum(t) is { } unknown)
            return $"{name}: {unknown}";

        if (t.InteractionTimeMs > MaxInteractionTimeMs)
            return $"{name}: InteractionTimeMs {t.InteractionTimeMs} is above {MaxInteractionTimeMs}";

        if (!float.IsFinite(t.InteractionRange) || t.InteractionRange <= 0f || t.InteractionRange > MaxInteractionRange)
            return $"{name}: InteractionRange {t.InteractionRange} is not above 0 and at most {MaxInteractionRange}";

        if (!float.IsFinite(t.AreaRadius) || t.AreaRadius < 0f || t.AreaRadius > MaxAreaRadius)
            return $"{name}: AreaRadius {t.AreaRadius} is not from 0 to {MaxAreaRadius}";

        bool solid = t.Collision != DoodadCollision.None;
        if (solid)
        {
            if (!float.IsFinite(t.CollisionHalfX) || !float.IsFinite(t.CollisionHalfZ) || !float.IsFinite(t.CollisionHeight)
                || t.CollisionHalfX <= 0f || t.CollisionHalfZ <= 0f || t.CollisionHeight < MinSolidHeight)
            {
                return $"{name}: a solid doodad's box ({t.CollisionHalfX}, {t.CollisionHalfZ}, {t.CollisionHeight} high) " +
                       $"needs finite half extents above 0 and a height of at least {MinSolidHeight}";
            }

            if (HasGate(t.VisibleQuestId, t.VisibleCondition, t.VisibleObjectiveId)
                || HasGate(t.UsableQuestId, t.UsableCondition, t.UsableObjectiveId))
            {
                return $"{name}: a solid doodad cannot be gated, since a hidden one would be an unseen wall";
            }

            if (t.SpentLook == DoodadSpentLook.Despawn)
                return $"{name}: a solid doodad cannot despawn, since its box stays in the navmesh";
        }

        if (t.RespawnSeconds == 0)
            return $"{name}: RespawnSeconds is 0; null never respawns";

        if (GateProblem("visible", t.VisibleQuestId, t.VisibleCondition, t.VisibleObjectiveId, objectivesByQuest) is { } visible)
            return $"{name}: {visible}";

        if (GateProblem("usable", t.UsableQuestId, t.UsableCondition, t.UsableObjectiveId, objectivesByQuest) is { } usable)
            return $"{name}: {usable}";

        if (t.ScriptName is { Length: > 0 } script && (scriptType is null || !scriptType.IsSubclassOf(typeof(DoodadScript))))
            return $"{name}: names doodad script '{script}', which is not loaded";

        if (t.LootTableId is { } table && !loot.TryGet(table, out _))
            return $"{name}: names loot table {table.Value}, which is not loaded";

        if (t.MinGold > t.MaxGold)
            return $"{name}: MinGold {t.MinGold} is above MaxGold {t.MaxGold}";

        if (t.RestorePct > 100)
            return $"{name}: RestorePct {t.RestorePct} is above 100";

        ulong total = 0;
        foreach (DoodadAuraOutcome outcome in t.AuraOutcomes)
        {
            if (outcome.AuraId is null || !auraIds.Contains(outcome.AuraId.Value))
                return $"{name}: an outcome names aura {outcome.AuraId?.Value}, which is not loaded";

            if (outcome.Weight == 0)
                return $"{name}: the outcome of aura {outcome.AuraId.Value} has weight 0";

            total += outcome.Weight;
        }

        return total > uint.MaxValue ? $"{name}: its outcome weights total {total}, above {uint.MaxValue}" : null;
    }

    /// <summary>The first model kit, key or clip that is not a plain asset name, named; null when all are.</summary>
    private static string? NotPlain(DoodadTemplate t)
    {
        if (!AssetNames.IsPlain(t.ModelKit))
            return $"ModelKit '{t.ModelKit}'";
        if (!AssetNames.IsPlain(t.ModelKey))
            return $"ModelKey '{t.ModelKey}'";
        if (t.UsedModelKey is not null && !AssetNames.IsPlain(t.UsedModelKey))
            return $"UsedModelKey '{t.UsedModelKey}'";
        if (t.ReadyClip is not null && !AssetNames.IsPlain(t.ReadyClip))
            return $"ReadyClip '{t.ReadyClip}'";
        if (t.UseClip is not null && !AssetNames.IsPlain(t.UseClip))
            return $"UseClip '{t.UseClip}'";
        if (t.UsedClip is not null && !AssetNames.IsPlain(t.UsedClip))
            return $"UsedClip '{t.UsedClip}'";
        return null;
    }

    /// <summary>The first enumerated value this build does not know, or a gate without its quest or condition.</summary>
    private static string? UnknownEnum(DoodadTemplate t)
    {
        if (!Enum.IsDefined(t.Interaction))
            return $"Interaction {(byte)t.Interaction} is unknown";
        if (!Enum.IsDefined(t.UseLimit))
            return $"UseLimit {(byte)t.UseLimit} is unknown";
        if (!Enum.IsDefined(t.SpentLook))
            return $"SpentLook {(byte)t.SpentLook} is unknown";
        if (!Enum.IsDefined(t.Collision))
            return $"Collision {(byte)t.Collision} is unknown";
        return GateEnum("visible", t.VisibleQuestId, t.VisibleCondition, t.VisibleObjectiveId)
               ?? GateEnum("usable", t.UsableQuestId, t.UsableCondition, t.UsableObjectiveId);
    }

    private static string? GateEnum(string gate, uint? questId, DoodadGateCondition? condition, uint? objectiveId)
    {
        if (!HasGate(questId, condition, objectiveId))
            return null;
        if (questId is null || condition is null)
            return $"its {gate} gate needs both a quest and a condition";
        return Enum.IsDefined(condition.Value) ? null : $"its {gate} gate's condition {(byte)condition.Value} is unknown";
    }

    private static string? GateProblem(string gate, uint? questId, DoodadGateCondition? condition, uint? objectiveId,
        Dictionary<uint, HashSet<uint>> objectivesByQuest)
    {
        if (questId is not { } quest || condition is not { } kind)
            return null;

        if (!objectivesByQuest.TryGetValue(quest, out HashSet<uint>? objectives))
            return $"its {gate} gate names quest {quest}, which is not loaded";

        if (kind == DoodadGateCondition.OnObjective)
        {
            if (objectiveId is not { } objective)
                return $"its {gate} gate is OnObjective but names no objective";
            if (!objectives.Contains(objective))
                return $"its {gate} gate names objective {objective}, which is not one of quest {quest}'s";
        }
        else if (objectiveId is not null)
        {
            return $"its {gate} gate is {kind} but names objective {objectiveId}; only OnObjective names one";
        }

        return null;
    }

    private static bool HasGate(uint? questId, DoodadGateCondition? condition, uint? objectiveId) =>
        questId is not null || condition is not null || objectiveId is not null;

    private static DoodadGate? Gate(uint? questId, DoodadGateCondition? condition, uint? objectiveId) =>
        questId is { } quest && condition is { } kind ? new DoodadGate(quest, kind, objectiveId) : null;

    private static DoodadTemplateView View(DoodadTemplate t, Type? scriptType, bool scriptCanSee)
    {
        DoodadOutcomeView[] outcomes = t.AuraOutcomes.OrderBy(o => o.AuraId.Value)
            .Select(o => new DoodadOutcomeView(o.AuraId, o.Weight)).ToArray();
        uint total = 0;
        foreach (DoodadOutcomeView outcome in outcomes)
            total += outcome.Weight;

        return new DoodadTemplateView(
            t.Id, t.Name, t.ModelKit, t.ModelKey, t.UsedModelKey, t.ReadyClip, t.UseClip, t.UsedClip,
            t.Interaction, t.InteractionTimeMs, t.InteractionRange, t.AreaRadius,
            t.UseLimit, t.SpentLook, t.RespawnSeconds, scriptType,
            t.LootTableId, t.MinGold, t.MaxGold, t.RestorePct,
            t.Collision, t.CollisionHalfX, t.CollisionHalfZ, t.CollisionHeight,
            Gate(t.VisibleQuestId, t.VisibleCondition, t.VisibleObjectiveId),
            Gate(t.UsableQuestId, t.UsableCondition, t.UsableObjectiveId),
            outcomes, total, scriptCanSee);
    }
}
