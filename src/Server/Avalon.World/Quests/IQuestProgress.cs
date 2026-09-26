using Avalon.Domain.World;
using Avalon.World.Entities;

namespace Avalon.World.Quests;

/// <summary>
/// Whether a character's quest is in a state (spec #432). Vendors ask it about quest-gated stock
/// rows, and a gated row is left out of the list and cannot be bought while it answers false. The
/// quest system (#433) implements it; until then only <see cref="NoQuestProgress" /> exists.
/// Tick thread only.
/// </summary>
public interface IQuestProgress
{
    bool IsMet(CharacterEntity character, uint questId, QuestRequirementState state);
}

/// <summary>No quests exist yet, so no requirement is met and every gated row stays hidden.</summary>
public sealed class NoQuestProgress : IQuestProgress
{
    public static readonly NoQuestProgress Instance = new();

    public bool IsMet(CharacterEntity character, uint questId, QuestRequirementState state) => false;
}
