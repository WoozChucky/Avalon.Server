using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Quests;

namespace Avalon.World.Doodads;

/// <summary>
/// Whether one viewer may see a doodad, and whether it may use it: the rules a view decision asks of a doodad that has
/// any (<see cref="AsksRules" />), and only when the viewer's quests or the doodad moved since it last asked
/// (CharacterCharacterGameState). A singleton of the container, tick thread only. Data gates only for now; the doodad
/// script's own say is the doodad runtime's. World-side: what a client is told exists is never a script's to grant.
/// </summary>
public sealed class DoodadVisibility
{
    /// <summary>
    /// Whether an answer about <paramref name="doodad" /> can differ between viewers: a visibility gate, a script that
    /// decides who sees it, or a usability gate. A doodad without one is seen by everyone in range, and usable by
    /// whoever sees it while <see cref="Usable" />, so no viewer's rules are asked of it.
    /// </summary>
    public static bool AsksRules(Doodad doodad) => doodad.HasViewRule || doodad.UsableGate is not null;

    /// <summary>
    /// What a doodad's own state allows, whoever asks: a click doodad that is ready, or that a use never spends, and not
    /// despawned.
    /// </summary>
    public static bool Usable(Doodad doodad)
    {
        DoodadTemplateView template = doodad.Template;
        return doodad.Visible && template.Interaction == DoodadInteraction.Click
               && (template.UseLimit == DoodadUseLimit.Unlimited || doodad.State == DoodadState.Ready);
    }

    /// <summary>
    /// Whether <paramref name="viewer" /> may be told <paramref name="doodad" /> exists: not despawned, and let through by
    /// its visibility gate.
    /// </summary>
    public bool IsVisibleTo(Doodad doodad, CharacterEntity viewer, QuestCatalog quests) =>
        doodad.Visible && DoodadGates.Allows(doodad.VisibleGate, viewer.Quests, quests);

    /// <summary>
    /// Whether <paramref name="viewer" />'s client should offer to use <paramref name="doodad" />: <see cref="Usable" />,
    /// let through by its usability gate, and visible to it. A doodad hidden from a viewer is never usable by it.
    /// </summary>
    public bool CanInteract(Doodad doodad, CharacterEntity viewer, QuestCatalog quests) =>
        Usable(doodad) && DoodadGates.Allows(doodad.UsableGate, viewer.Quests, quests)
                       && IsVisibleTo(doodad, viewer, quests);

    /// <summary>
    /// <see cref="IsVisibleTo" /> and <see cref="CanInteract" /> together, at the doodad's current
    /// <see cref="Doodad.ViewVersion" />, each rule asked once: what a view decision holds for the viewer.
    /// </summary>
    internal DoodadAnswer Answer(Doodad doodad, CharacterEntity viewer, QuestCatalog quests)
    {
        bool visible = IsVisibleTo(doodad, viewer, quests);
        bool canInteract = visible && Usable(doodad) && DoodadGates.Allows(doodad.UsableGate, viewer.Quests, quests);
        return new DoodadAnswer(visible, canInteract, doodad.ViewVersion);
    }
}
