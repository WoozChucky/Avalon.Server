using Avalon.Domain.Characters;
using Avalon.World;
using Avalon.World.Characters;
using Avalon.World.Entities;
using Avalon.World.Talents;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>A character entering the world with saved talents (#1181), as select leaves it.</summary>
internal static class TalentTestCharacters
{
    /// <summary>
    /// Loads <paramref name="held" />, sums it, refreshes the stats as select does, builds the abilities the tree grants,
    /// records the talent state as sent at the character's level, and acknowledges every save mark, so what follows starts
    /// from a character with nothing to save or send.
    /// </summary>
    public static void Enter(CharacterEntity character, StaticData data, params int[] held)
    {
        character.Talents.Load([.. held.Select(n => new CharacterTalent { CharacterId = character.Data!.Id, NodeId = n })]);
        character.Talents.RebuildTotals(data.Talents);
        Assert.True(CharacterStatsRefresh.Apply(character, data, CurrentValues.EnterWorld));
        TalentAbilities.Rebuild(character, data.Talents, data.Abilities, NullLogger.Instance);
        character.Talents.StateSent(character.Level);
        character.SaveState.Acknowledge(character.SaveState.TakeMarks());
    }
}
