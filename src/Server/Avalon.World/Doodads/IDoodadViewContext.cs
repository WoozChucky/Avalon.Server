using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Doodads;

/// <summary>
/// What <see cref="DoodadScript.CanSee" /> may read: the doodad, what its data gate answered, and the viewer's name,
/// level, class and quests. Read only and allocation-free; one reusable instance, valid during the call alone (any
/// member used after it throws <see cref="InvalidOperationException" />). World-side, not the modding API.
/// </summary>
public interface IDoodadViewContext
{
    DoodadTemplateId TemplateId { get; }

    Vector3 Position { get; }

    DoodadState State { get; }

    /// <summary>What the doodad's visibility gate (the spawn's, else the template's) answered; true with none.</summary>
    bool DataGateAllows { get; }

    string ViewerName { get; }

    ushort ViewerLevel { get; }

    CharacterClass ViewerClass { get; }

    /// <summary>Whether the viewer holds the quest: accepted and not turned in.</summary>
    bool ViewerHolds(uint questId);

    /// <summary>Whether the viewer turned the quest in.</summary>
    bool ViewerCompleted(uint questId);

    /// <summary>Whether the viewer is on the objective: the OnObjective rule of <see cref="DoodadGates" />.</summary>
    bool ViewerOnObjective(uint questId, uint objectiveId);
}
