using Avalon.Domain.World;
using Avalon.World.Entities;

namespace Avalon.World.Quests;

/// <summary>
/// Whether a character's quest is in a state (spec #432). Vendors ask it about quest-gated stock
/// rows, and a gated row is left out of the list and cannot be bought while it answers false.
/// <see cref="QuestProgress" /> implements it over the character's quest log (#433);
/// <see cref="NoQuestProgress" />, which meets nothing, is what a handler built without the
/// container falls back to.
/// Tick thread only.
/// </summary>
public interface IQuestProgress
{
    bool IsMet(CharacterEntity character, uint questId, QuestRequirementState state);
}

/// <summary>Meets no requirement, so every gated row stays hidden. The fallback without the container.</summary>
public sealed class NoQuestProgress : IQuestProgress
{
    public static readonly NoQuestProgress Instance = new();

    public bool IsMet(CharacterEntity character, uint questId, QuestRequirementState state) => false;
}
