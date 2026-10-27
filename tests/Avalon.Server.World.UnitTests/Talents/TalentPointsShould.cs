using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.World.Persistence;
using Avalon.World.Quests;
using Avalon.World.Talents;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>
/// #1181: a character earns a talent point per level and the points each quest it turned in pays, over the seeded quest
/// catalog: quests 3 (The Alpha's Howl), 7 and 8 pay one each; the repeatable Boar Cull (10) pays none, and a quest the
/// catalog does not hold pays none.
/// </summary>
public class TalentPointsShould
{
    [Theory]
    [InlineData((ushort)1, new uint[0], 1)]
    [InlineData((ushort)2, new uint[] { 3 }, 3)]
    [InlineData((ushort)7, new uint[] { 3, 7, 8 }, 10)]
    [InlineData((ushort)15, new uint[] { 3, 7, 8, 10 }, 18)]
    [InlineData((ushort)5, new uint[] { 3, 999 }, 6)]
    public void Earn_a_point_per_level_and_per_paying_quest(ushort level, uint[] completed, int earned)
    {
        var quests = new QuestLog(new SaveStateTracker());
        CharacterId owner = 118_101u;
        quests.Load(new CharacterQuestRows([], [],
            [.. completed.Select(id => new CharacterCompletedQuest { CharacterId = owner, QuestId = id, CompletionCount = 1 })]));

        Assert.Equal(earned, TalentPoints.Earned(level, quests, ScenarioReferenceData.Shared.Data.Quests));
    }
}
