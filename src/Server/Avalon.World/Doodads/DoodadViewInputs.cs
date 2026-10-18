using Avalon.World.Entities;
using Avalon.World.Quests;

namespace Avalon.World.Doodads;

/// <summary>
/// What one view decision needs to list doodads: the instance's index, the viewer whose rules are asked, the quest
/// catalog its gates read, and the rules themselves. The default lists none, as an instance without doodads passes.
/// </summary>
public readonly record struct DoodadViewInputs(DoodadIndex? Index, CharacterEntity? Viewer, QuestCatalog? Quests,
    DoodadVisibility? Visibility);

/// <summary>
/// One viewer's last answer about one doodad that asks its rules (<see cref="DoodadVisibility.AsksRules" />): whether it
/// sees it, whether it can use it, and the doodad's <see cref="Doodad.ViewVersion" /> it was decided at.
/// </summary>
public readonly record struct DoodadAnswer(bool Visible, bool CanInteract, int ViewVersion);
