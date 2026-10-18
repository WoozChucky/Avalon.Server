using Avalon.Domain.World;
using Avalon.World.Entities;
using Avalon.World.Quests;

namespace Avalon.World.Doodads;

/// <summary>
/// Whether one viewer may see a doodad, and whether it may use it: the rules a view decision asks of a doodad that has
/// any (<see cref="AsksRules" />), and only when the viewer's quests or the doodad moved since it last asked
/// (CharacterCharacterGameState). The data gates first, then the doodad script's say (<see cref="DoodadScript.CanSee" />)
/// for a template whose script declares one. A singleton of the container, tick thread only: it keeps the one view
/// context every script is asked through. World-side: what a client is told exists is never a script's to grant beyond
/// its own doodad, and never for a solid one.
/// </summary>
/// <param name="scripts">The doodad scripts asked; none is asked without them, as in a test that places no scripted doodad.</param>
public sealed class DoodadVisibility(DoodadScripts? scripts = null)
{
    private const string CanSeeHook = nameof(DoodadScript.CanSee);

    private readonly DoodadViewContext _view = new();

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
    /// Whether <paramref name="viewer" /> may be told <paramref name="doodad" /> exists: not despawned; then its
    /// visibility gate's answer, unless its script's <see cref="DoodadScript.CanSee" /> overrides it. A script that throws
    /// is logged, at most once per ThrottledErrorLog.Interval, and the gate's answer stands; nothing escapes.
    /// </summary>
    public bool IsVisibleTo(Doodad doodad, CharacterEntity viewer, QuestCatalog quests)
    {
        if (!doodad.Visible)
            return false;

        bool data = DoodadGates.Allows(doodad.VisibleGate, viewer.Quests, quests);
        return doodad.AsksScript && scripts?.For(doodad.Template) is { } script
            ? AskScript(script, doodad, viewer, quests, data)
            : data;
    }

    /// <summary>
    /// Whether <paramref name="viewer" />'s client should offer to use <paramref name="doodad" />: visible to it,
    /// <see cref="Usable" /> and let through by its usability gate. A doodad hidden from a viewer is never usable by it.
    /// </summary>
    public bool CanInteract(Doodad doodad, CharacterEntity viewer, QuestCatalog quests) =>
        Interactable(IsVisibleTo(doodad, viewer, quests), doodad, viewer, quests);

    /// <summary>
    /// <see cref="IsVisibleTo" /> and <see cref="CanInteract" /> together, at the doodad's current
    /// <see cref="Doodad.ViewVersion" />, each rule asked once: what a view decision holds for the viewer.
    /// </summary>
    internal DoodadAnswer Answer(Doodad doodad, CharacterEntity viewer, QuestCatalog quests)
    {
        bool visible = IsVisibleTo(doodad, viewer, quests);
        return new DoodadAnswer(visible, Interactable(visible, doodad, viewer, quests), doodad.ViewVersion);
    }

    /// <summary>
    /// Past being seen, why <paramref name="viewer" /> may not use <paramref name="doodad" />: its usability gate refuses
    /// (<see cref="DoodadUseRefusal.NotUsable" />), or its own state does (not <see cref="Usable" />:
    /// <see cref="DoodadUseRefusal.Spent" />); null when it may. The one statement of "can use" past visibility, asked by
    /// <see cref="CanInteract" />, by a view decision and by an interact's checks (DoodadUseService), so what a client is
    /// offered and what an interact is let through by never differ.
    /// </summary>
    internal static DoodadUseRefusal? UseRefusal(Doodad doodad, CharacterEntity viewer, QuestCatalog quests) =>
        !DoodadGates.Allows(doodad.UsableGate, viewer.Quests, quests) ? DoodadUseRefusal.NotUsable
        : Usable(doodad) ? null
        : DoodadUseRefusal.Spent;

    private static bool Interactable(bool visible, Doodad doodad, CharacterEntity viewer, QuestCatalog quests) =>
        visible && UseRefusal(doodad, viewer, quests) is null;

    private bool AskScript(DoodadScript script, Doodad doodad, CharacterEntity viewer, QuestCatalog quests, bool data)
    {
        _view.Open(doodad, viewer, quests, data);
        try
        {
            return script.CanSee(_view) ?? data;
        }
        catch (Exception e)
        {
            scripts!.Failed(script.GetType(), CanSeeHook, e);
            return data;
        }
        finally
        {
            _view.Close();
        }
    }
}
