using Avalon.Common.ValueObjects;

namespace Avalon.Domain.World;

/// <summary>
/// One doodad rule of a procedural map: how many doodads of a template its runs place, at which chunk doodad slots (by
/// tag and depth), how far apart, and whether the map must have them (a required rule falls back to open navmesh when no
/// slot fits). A gate set here replaces the template's for the doodads this rule places. Owned by ProceduralMapConfig,
/// keyed by (MapTemplateId, Sequence), and written by ChunkCatalogSeeder from Maps/ProceduralMaps/&lt;mapId&gt;.json, its
/// Sequence the rule's place in the file from 1.
/// </summary>
public class ProceduralDoodadRule
{
    /// <summary>The rule's place in its file, from 1: the order rules of the same kind are planned in.</summary>
    public int Sequence { get; set; }

    public DoodadTemplateId DoodadTemplateId { get; set; } = null!;

    /// <summary>The chunk doodad slot tag it places at (<c>doodad</c>, <c>doodad_large</c>).</summary>
    public string SlotTag { get; set; } = string.Empty;

    /// <summary>The fewest doodads a run places: 0 to MaxCount.</summary>
    public byte MinCount { get; set; }

    /// <summary>The most doodads a run places: MinCount to 50.</summary>
    public byte MaxCount { get; set; }

    /// <summary>Planned before every optional rule, and placed on open navmesh when its slots run short.</summary>
    public bool Required { get; set; }

    /// <summary>The least distance, in metres on X/Z, between two doodads of this template: finite, 0 or more.</summary>
    public float MinSpacing { get; set; }

    /// <summary>The shallowest piece it places in (grid steps from the entry); null from the entry on.</summary>
    public int? MinDepth { get; set; }

    /// <summary>The deepest piece it places in; null without end.</summary>
    public int? MaxDepth { get; set; }

    /// <summary>The spawn's own visibility gate quest; with VisibleCondition, it replaces the template's gate.</summary>
    public uint? VisibleQuestId { get; set; }

    /// <summary>Set exactly when VisibleQuestId is.</summary>
    public DoodadGateCondition? VisibleCondition { get; set; }

    /// <summary>The objective an OnObjective visibility gate names.</summary>
    public uint? VisibleObjectiveId { get; set; }

    /// <summary>The spawn's own usability gate quest; with UsableCondition, it replaces the template's gate.</summary>
    public uint? UsableQuestId { get; set; }

    /// <summary>Set exactly when UsableQuestId is.</summary>
    public DoodadGateCondition? UsableCondition { get; set; }

    /// <summary>The objective an OnObjective usability gate names.</summary>
    public uint? UsableObjectiveId { get; set; }
}
