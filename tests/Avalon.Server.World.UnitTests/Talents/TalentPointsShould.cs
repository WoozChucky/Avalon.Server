using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.World.Configuration;
using Avalon.World.Persistence;
using Avalon.World.Quests;
using Avalon.World.Talents;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Talents;

/// <summary>
/// #1181: a character earns a talent point per level and the points each quest it turned in pays, over the seeded quest
/// catalog: quests 3 (The Alpha's Howl), 7 and 8 pay one each; the repeatable Boar Cull (10) pays none, and a quest the
/// catalog does not hold pays none. A refund is free up to Game:Talents:FreeRefundMaxLevel and costs, per node, the base
/// price plus the per-level price for each level past the first paying one; a price above Game:MaxGold stops the start.
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

    [Theory]
    [InlineData((ushort)10, (ushort)10, 100UL, 25UL, 1, 0UL, 0UL)]
    [InlineData((ushort)11, (ushort)10, 100UL, 25UL, 1, 100UL, 100UL)]
    [InlineData((ushort)12, (ushort)10, 100UL, 25UL, 1, 125UL, 125UL)]
    [InlineData((ushort)15, (ushort)10, 100UL, 25UL, 1, 200UL, 200UL)]
    [InlineData((ushort)30, (ushort)10, 100UL, 25UL, 1, 575UL, 575UL)]
    [InlineData((ushort)50, (ushort)10, 100UL, 25UL, 1, 1075UL, 1075UL)]
    [InlineData((ushort)100, (ushort)10, 100UL, 25UL, 1, 2325UL, 2325UL)]
    [InlineData((ushort)12, (ushort)10, 100UL, 25UL, 2, 125UL, 250UL)]
    [InlineData((ushort)1, (ushort)0, 100UL, 25UL, 1, 100UL, 100UL)]
    [InlineData((ushort)65535, (ushort)10, 10_000_000_000UL, 10_000_000_000UL, 1, 655_250_000_000_000UL,
        655_250_000_000_000UL)]
    [InlineData((ushort)100, (ushort)10, 9_223_372_036_854_775_807UL, 0UL, 3, 9_223_372_036_854_775_807UL,
        ulong.MaxValue)]
    public void Price_a_refund_free_to_the_free_level_and_linear_after(ushort level, ushort freeMax, ulong basePrice,
        ulong perLevel, int nodes, ulong perNode, ulong total)
    {
        var config = new TalentConfiguration
        {
            FreeRefundMaxLevel = freeMax,
            RefundBasePrice = basePrice,
            RefundPricePerLevel = perLevel,
        };

        ulong priced = TalentPrice.PerNode(level, config);

        Assert.Equal((perNode, total), (priced, TalentPrice.Total(priced, nodes)));
    }

    [Theory]
    [InlineData(100UL, 25UL, true)]
    [InlineData(9_999_999_999UL, 9_999_999_999UL, true)]
    [InlineData(10_000_000_000UL, 25UL, false)]
    [InlineData(100UL, 10_000_000_000UL, false)]
    public void Refuse_refund_prices_above_the_gold_cap(ulong basePrice, ulong perLevel, bool accepted)
    {
        var options = new GameConfiguration
        {
            Talents = new TalentConfiguration { RefundBasePrice = basePrice, RefundPricePerLevel = perLevel },
        };

        Assert.Equal(accepted, new TalentConfigurationValidation().Validate(null, options).Succeeded);
    }
}
